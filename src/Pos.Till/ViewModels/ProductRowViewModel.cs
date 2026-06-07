using Pos.Till.Api;

namespace Pos.Till.ViewModels;

/// <summary>A read-only catalogue row. Pure presentation over a ProductDto from the API.</summary>
public sealed class ProductRowViewModel(ProductDto product)
{
    public Guid Id => product.Id;
    public string Sku => product.Sku;
    public string Name => product.Name;
    public string? Barcode => product.Barcode;
    public Guid? CategoryId => product.CategoryId;
    public decimal UnitPrice => product.Price.Amount;
    public string Currency => product.Price.Currency;
    public bool IsWeighed => product.UnitOfMeasure == UnitOfMeasure.Kg;

    public string Display => $"{Name}  ·  {Sku}";
    public string PriceDisplay => $"{Currency} {UnitPrice:0.00}{(IsWeighed ? " / kg" : "")}";

    public bool Matches(string term) =>
        Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Sku.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (Barcode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
}
