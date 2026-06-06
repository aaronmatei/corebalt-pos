using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Catalog;

/// <summary>
/// A sellable item. Carries StoreId (per-branch overrides allowed from day one) so we
/// don't have to migrate later when the chain wants per-branch pricing. The future
/// HQ-managed central catalog (roadmap M2) lands as a parent record this row derives from.
/// </summary>
public sealed class Product : AggregateRoot, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The printed scan code (GTIN / EAN-13 / UPC), distinct from the human SKU. Nullable
    /// because not every line is barcoded. Today a product carries at most one; the roadmap
    /// (multiple barcodes per product, price-embedded EAN-13 from scales) lands as a child
    /// table later, so callers should treat "look up by scanned code" as the stable contract
    /// rather than this single column.
    /// </summary>
    public string? Barcode { get; private set; }
    public Money Price { get; private set; } = Money.Zero();
    public UnitOfMeasure UnitOfMeasure { get; private set; }

    /// <summary>KRA VAT class. Drives how VAT is backed out of the (VAT-inclusive) price at checkout.</summary>
    public TaxClass TaxClass { get; private set; }
    public bool IsActive { get; private set; }

    private Product() { } // EF

    public static Product Create(Guid tenantId, Guid storeId, string sku, string name,
        Money price, UnitOfMeasure unit = UnitOfMeasure.Each, string? barcode = null,
        TaxClass taxClass = TaxClass.StandardRated)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("Sku is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (price.Amount < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");

        return new Product
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            StoreId = storeId,
            Sku = sku.Trim(),
            Name = name.Trim(),
            Barcode = NormalizeBarcode(barcode),
            Price = price,
            UnitOfMeasure = unit,
            TaxClass = taxClass,
            IsActive = true
        };
    }

    /// <summary>Set or clear the product's scan code. Blank input clears it (stored as null).</summary>
    public void AssignBarcode(string? barcode) => Barcode = NormalizeBarcode(barcode);

    private static string? NormalizeBarcode(string? barcode)
        => string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

    public void Reprice(Money newPrice)
    {
        if (newPrice.Currency != Price.Currency)
            throw new InvalidOperationException("Currency mismatch on reprice.");
        if (newPrice.Amount < 0) throw new ArgumentOutOfRangeException(nameof(newPrice));
        Price = newPrice;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
