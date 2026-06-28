namespace Pos.Domain.Catalog;

/// <summary>
/// Append-only feed of HQ catalogue changes (M2). One row per <see cref="CatalogItem"/> mutation,
/// carrying the FULL latest snapshot, ordered by a DB-assigned monotonic <see cref="Seq"/>. Branch
/// store-servers PULL rows with <c>Seq &gt; cursor</c> and upsert their local product by SKU — so a store
/// never has to be reachable; it catches up on its own cadence (at-least-once; upserts are idempotent).
/// Enum-ish fields are stored as strings for wire stability across versions.
/// </summary>
public sealed class CatalogChange
{
    public long Seq { get; private set; }            // identity, DB-assigned — the cursor
    public Guid TenantId { get; private set; }
    public Guid CatalogItemId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal PriceAmount { get; private set; }
    public string Currency { get; private set; } = "KES";
    public string TaxClass { get; private set; } = string.Empty;
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }

    private CatalogChange() { } // EF

    public static CatalogChange From(CatalogItem item, DateTimeOffset now) => new()
    {
        TenantId = item.TenantId,
        CatalogItemId = item.Id,
        Sku = item.Sku,
        Name = item.Name,
        PriceAmount = item.Price.Amount,
        Currency = item.Price.Currency,
        TaxClass = item.TaxClass.ToString(),
        UnitOfMeasure = item.UnitOfMeasure.ToString(),
        Barcode = item.Barcode,
        IsActive = item.IsActive,
        ChangedAtUtc = now,
    };
}
