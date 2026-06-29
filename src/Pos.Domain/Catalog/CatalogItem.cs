using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Catalog;

/// <summary>
/// The HQ/cloud canonical catalogue entry — tenant-level master data (M2). Owned at HQ and pushed DOWN
/// to every branch store-server, which upserts its own store-scoped <see cref="Product"/> by SKU. SKU is
/// the natural key (unique per tenant). Prices/name/tax here are AUTHORITATIVE for the chain (HQ wins);
/// stock is NEVER part of this — it stays store-owned. Each mutation is logged to <see cref="CatalogChange"/>
/// so stores can pull the delta. Eventless (it doesn't go through the store outbox).
/// </summary>
public sealed class CatalogItem : AggregateRoot, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Money Price { get; private set; } = Money.Zero();
    public TaxClass TaxClass { get; private set; }
    public UnitOfMeasure UnitOfMeasure { get; private set; }
    public string? Barcode { get; private set; }
    /// <summary>The chain-wide category name this item belongs to (null = uncategorized). A NAME, not an id,
    /// so it's portable to every branch DB — branches materialize their own local <c>Category</c> by name
    /// on pull (category ids are not shared across the cloud + on-prem databases).</summary>
    public string? CategoryName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private CatalogItem() { } // EF

    public static CatalogItem Create(Guid tenantId, string sku, string name, Money price,
        TaxClass taxClass, UnitOfMeasure unitOfMeasure, string? barcode, string? categoryName, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        return new CatalogItem
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            Sku = sku.Trim(),
            Name = name.Trim(),
            Price = price,
            TaxClass = taxClass,
            UnitOfMeasure = unitOfMeasure,
            Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
            CategoryName = string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim(),
            IsActive = true,
            UpdatedAtUtc = now,
        };
    }

    public void Update(string name, string? barcode, UnitOfMeasure unitOfMeasure, TaxClass taxClass, string? categoryName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        UnitOfMeasure = unitOfMeasure;
        TaxClass = taxClass;
        CategoryName = string.IsNullOrWhiteSpace(categoryName) ? null : categoryName.Trim();
        UpdatedAtUtc = now;
    }

    public void Reprice(Money price, DateTimeOffset now)
    {
        if (price.Amount < 0) throw new ArgumentException("Price cannot be negative.", nameof(price));
        Price = price;
        UpdatedAtUtc = now;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        UpdatedAtUtc = now;
    }
}
