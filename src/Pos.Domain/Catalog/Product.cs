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

    /// <summary>
    /// Reorder (low-stock) threshold. When set, the product is "low" once on-hand (SUM of movements)
    /// falls to/below this. Null = not tracked. Decimal so weighed goods work (e.g. 2.5 kg). Product-level
    /// for the single store today; in multi-branch this becomes per-(store, product) — the door is left
    /// open (no per-store table yet).
    /// </summary>
    public decimal? ReorderLevel { get; private set; }

    /// <summary>Suggested quantity to order when low (a reorder/"par" top-up). Null = no suggestion.</summary>
    public decimal? ReorderQuantity { get; private set; }

    /// <summary>
    /// Notification bookkeeping ONLY (never the low-stock status, which is always DERIVED from movements):
    /// true once a ProductLowStock event has fired for the current dip, so we alert ONCE per dip and not on
    /// every subsequent sale. Cleared when on-hand is lifted back above the level, arming the next dip.
    /// </summary>
    public bool LowStockNotified { get; private set; }

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
    /// Apply an authoritative HQ catalogue push (M2): overwrite name / barcode / unit / tax / price / active /
    /// category from the central catalogue. Deliberately raises NO domain event — it mirrors an external
    /// decision (not a local action), so it never churns the store outbox or loops back to HQ. Stock is
    /// untouched. <paramref name="categoryId"/> is the LOCAL category the puller resolved from the pushed name.
    /// </summary>
    public void ApplyHqCatalog(string name, string? barcode, UnitOfMeasure unit, TaxClass taxClass, Money price, bool active, Guid? categoryId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (price.Amount < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        Name = name.Trim();
        Barcode = NormalizeBarcode(barcode);
        UnitOfMeasure = unit;
        TaxClass = taxClass;
        Price = price;
        IsActive = active;
        CategoryId = categoryId;
    }

    /// <summary>
    /// Set (or clear) the reorder threshold + suggested order quantity. Changing the settings re-arms
    /// notification (clears <see cref="LowStockNotified"/>) so the next stock movement re-evaluates against
    /// the new level — an item left below a freshly-set level alerts again on its next dip.
    /// </summary>
    public void SetReorderSettings(decimal? reorderLevel, decimal? reorderQuantity)
    {
        if (reorderLevel is < 0) throw new ArgumentOutOfRangeException(nameof(reorderLevel), "Reorder level cannot be negative.");
        if (reorderQuantity is < 0) throw new ArgumentOutOfRangeException(nameof(reorderQuantity), "Reorder quantity cannot be negative.");
        ReorderLevel = reorderLevel;
        ReorderQuantity = reorderLevel is null ? null : reorderQuantity; // no suggestion without a level
        LowStockNotified = false;
    }

    /// <summary>
    /// Evaluate the reorder state against the product's NEW on-hand (caller supplies on-hand AFTER the
    /// movement). Fires <see cref="ProductLowStock"/> ONCE per dip — when on-hand sits at/below the level
    /// and we haven't already notified for this dip — and clears the notified flag when on-hand recovers
    /// above the level (arming the next dip). A no-op when the product isn't tracked (no reorder level).
    /// The event is drained to the outbox by the same SaveChanges that records the movement.
    /// </summary>
    public void EvaluateReorder(decimal onHand)
    {
        if (ReorderLevel is not { } level) return; // not tracked

        if (onHand <= level)
        {
            if (LowStockNotified) return; // already alerted for this dip — don't re-notify on further sales
            LowStockNotified = true;
            Raise(new ProductLowStock(Id, TenantId, StoreId, Sku, Name, onHand, level, ReorderQuantity, UnitOfMeasure));
        }
        else if (LowStockNotified)
        {
            LowStockNotified = false; // recovered above the level — re-arm for the next dip
        }
    }

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
