using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Persistence.Repositories;

/// <summary>
/// Atomic per-(tenant, store) receipt sequence via a single Postgres upsert: the row is inserted at
/// 1 or bumped by 1 and the new value RETURNed, all in one statement. Runs on the DbContext's
/// connection + current transaction, so it commits atomically with the sale (callers invoke it
/// inside IUnitOfWork.ExecuteInTransactionAsync). Concurrent checkouts for the same store serialize on
/// the row lock → consecutive numbers, no duplicates, no gaps from a rolled-back sale.
/// </summary>
internal sealed class ReceiptNumberSequence : IReceiptNumberSequence
{
    private readonly PosDbContext _db;
    public ReceiptNumberSequence(PosDbContext db) => _db = db;

    public async Task<long> NextAsync(Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText =
            """
            INSERT INTO receipt_counters (tenant_id, store_id, next_value)
            VALUES (@t, @s, 1)
            ON CONFLICT (tenant_id, store_id)
            DO UPDATE SET next_value = receipt_counters.next_value + 1
            RETURNING next_value;
            """;
        cmd.Parameters.Add(Param(cmd, "t", tenantId));
        cmd.Parameters.Add(Param(cmd, "s", storeId));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private static DbParameter Param(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }
}
