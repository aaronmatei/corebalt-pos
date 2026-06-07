using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Pos.Domain.Catalog.Events;

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

    /// <summary>
    /// Optional tenant-scoped <see cref="Category"/> this product belongs to (at most one). Null =
    /// "Uncategorized" — so existing products keep working and sell with no category. A loose Guid
    /// reference (no navigation), keeping the catalogue aggregate boundary clean like the other ids.
    /// </summary>
    public Guid? CategoryId { get; private set; }
    public bool IsActive { get; private set; }

    private Product() { } // EF

    public static Product Create(Guid tenantId, Guid storeId, string sku, string name,
        Money price, UnitOfMeasure unit = UnitOfMeasure.Each, string? barcode = null,
        TaxClass taxClass = TaxClass.StandardRated, Guid? categoryId = null)
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
            CategoryId = categoryId,
            IsActive = true
        };
    }

    /// <summary>Set or clear the product's scan code. Blank input clears it (stored as null).</summary>
    public void AssignBarcode(string? barcode) => Barcode = NormalizeBarcode(barcode);

    private static string? NormalizeBarcode(string? barcode)
        => string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

    /// <summary>Edit the catalogue details (not price — price changes go through <see cref="Reprice"/>).</summary>
    public void UpdateDetails(string name, string? barcode, UnitOfMeasure unit, TaxClass taxClass, Guid? categoryId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
        Barcode = NormalizeBarcode(barcode);
        UnitOfMeasure = unit;
        TaxClass = taxClass;
        CategoryId = categoryId;
    }

    /// <summary>Move the product into a category (or clear it — null = Uncategorized).</summary>
    public void AssignCategory(Guid? categoryId) => CategoryId = categoryId;

    /// <summary>
    /// Change the price. Never silent: a real change RAISES <see cref="ProductPriceChanged"/> (drained
    /// to the outbox for audit + central-pricing seam). The new price stays on the product for fast
    /// lookup. A no-op (same amount) raises nothing.
    /// </summary>
    public void Reprice(Money newPrice, Guid changedBy)
    {
        if (newPrice.Currency != Price.Currency)
            throw new InvalidOperationException("Currency mismatch on reprice.");
        if (newPrice.Amount < 0) throw new ArgumentOutOfRangeException(nameof(newPrice));
        if (newPrice.Amount == Price.Amount) return; // unchanged — don't emit a spurious event

        var old = Price;
        Price = newPrice;
        Raise(new ProductPriceChanged(Id, TenantId, StoreId, old.Amount, newPrice.Amount, Price.Currency, changedBy));
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
