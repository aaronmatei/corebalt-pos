using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Till.Api;

namespace Pos.Till.Services.Local;

/// <summary>A sale captured while the till was offline, awaiting replay to the store server.</summary>
public sealed record QueuedSale(Guid SaleId, string Payload, DateTimeOffset CreatedUtc);

/// <summary>
/// The till's on-disk safety net for when the LAN/store server is unreachable (invariant: a till must
/// keep selling when the network drops). Two responsibilities, both backed by a single local SQLite file:
///  1. <b>Catalogue cache</b> — the last products the till saw online, so it can still list + barcode-look-up
///     while offline.
///  2. <b>Offline sale queue</b> — cash sales taken offline, each keyed by the edge-generated UUIDv7 the till
///     stamped on it, drained back to <c>/sales/checkout</c> on reconnect. Because the server's checkout is
///     idempotent on that id, a sale can be re-sent safely (a dropped response never double-charges).
/// Holds ONE connection for its lifetime and serialises access (the UI thread and the background drain both
/// touch it), which is ample for a single-lane till.
/// </summary>
public sealed class LocalStore : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Open (or create) the local store at <paramref name="dbPath"/> — a file path, or ":memory:" in tests.</summary>
    public LocalStore(string dbPath)
    {
        // Pooling=false so ":memory:" stays alive for the connection's lifetime (each pooled open is a fresh db).
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        }.ToString());
        _conn.Open();
    }

    /// <summary>The default per-machine location for the till's offline database (under LocalAppData).</summary>
    public static string DefaultDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Corebalt POS Till");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "till-offline.db");
    }

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS products (
                    id      TEXT PRIMARY KEY,
                    barcode TEXT,
                    json    TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_products_barcode ON products(barcode);
                CREATE TABLE IF NOT EXISTS queued_sales (
                    sale_id     TEXT PRIMARY KEY,
                    payload     TEXT NOT NULL,
                    created_utc TEXT NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    // ── Catalogue cache ─────────────────────────────────────────────────────────────────────────
    /// <summary>Replace the cached catalogue with the latest list pulled while online.</summary>
    public async Task CacheProductsAsync(IReadOnlyList<ProductDto> products)
    {
        await _gate.WaitAsync();
        try
        {
            await using var tx = _conn.BeginTransaction();
            await using (var del = _conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM products;";
                await del.ExecuteNonQueryAsync();
            }
            foreach (var p in products)
            {
                await using var ins = _conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO products(id, barcode, json) VALUES ($id, $bc, $json);";
                ins.Parameters.AddWithValue("$id", p.Id.ToString());
                ins.Parameters.AddWithValue("$bc", (object?)p.Barcode ?? DBNull.Value);
                ins.Parameters.AddWithValue("$json", JsonSerializer.Serialize(p, Json));
                await ins.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ProductDto>> GetCachedProductsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var list = new List<ProductDto>();
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM products;";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var dto = JsonSerializer.Deserialize<ProductDto>(r.GetString(0), Json);
                if (dto is not null) list.Add(dto);
            }
            return list;
        }
        finally { _gate.Release(); }
    }

    public async Task<ProductDto?> FindByBarcodeAsync(string barcode)
    {
        await _gate.WaitAsync();
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM products WHERE barcode = $bc LIMIT 1;";
            cmd.Parameters.AddWithValue("$bc", barcode);
            var json = await cmd.ExecuteScalarAsync() as string;
            return json is null ? null : JsonSerializer.Deserialize<ProductDto>(json, Json);
        }
        finally { _gate.Release(); }
    }

    // ── Offline sale queue ──────────────────────────────────────────────────────────────────────
    /// <summary>Queue a sale taken offline. Idempotent on the sale id, so re-queuing is harmless.</summary>
    public async Task EnqueueSaleAsync(Guid saleId, string payloadJson, DateTimeOffset nowUtc)
    {
        await _gate.WaitAsync();
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO queued_sales(sale_id, payload, created_utc)
                VALUES ($id, $payload, $created);
                """;
            cmd.Parameters.AddWithValue("$id", saleId.ToString());
            cmd.Parameters.AddWithValue("$payload", payloadJson);
            cmd.Parameters.AddWithValue("$created", nowUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Queued sales oldest-first, so they replay in the order they were taken.</summary>
    public async Task<IReadOnlyList<QueuedSale>> GetQueuedSalesAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var list = new List<QueuedSale>();
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT sale_id, payload, created_utc FROM queued_sales ORDER BY created_utc;";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new QueuedSale(Guid.Parse(r.GetString(0)), r.GetString(1),
                    DateTimeOffset.Parse(r.GetString(2))));
            return list;
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveQueuedSaleAsync(Guid saleId)
    {
        await _gate.WaitAsync();
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM queued_sales WHERE sale_id = $id;";
            cmd.Parameters.AddWithValue("$id", saleId.ToString());
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task<int> QueuedCountAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM queued_sales;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.DisposeAsync();
        _gate.Dispose();
    }
}
