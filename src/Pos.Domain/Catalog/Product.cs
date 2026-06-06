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
    public Money Price { get; private set; } = Money.Zero();
    public UnitOfMeasure UnitOfMeasure { get; private set; }
    public bool IsActive { get; private set; }

    private Product() { } // EF

    public static Product Create(Guid tenantId, Guid storeId, string sku, string name,
        Money price, UnitOfMeasure unit = UnitOfMeasure.Each)
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
            Price = price,
            UnitOfMeasure = unit,
            IsActive = true
        };
    }

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
