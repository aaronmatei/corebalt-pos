namespace Pos.Till.ViewModels;

/// <summary>
/// A line in the basket. Quantity/weight is fixed when the line is added (via the add panel),
/// so this is immutable presentation. LineTotal here is a CLIENT-SIDE preview only — the API's
/// checkout response is the authoritative total.
/// </summary>
public sealed class CartLineViewModel
{
    public Guid ProductId { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public string Currency { get; }
    public bool IsWeighed { get; }

    public CartLineViewModel(ProductRowViewModel product, decimal quantity)
    {
        ProductId = product.Id;
        Description = product.Name;
        Quantity = quantity;
        UnitPrice = product.UnitPrice;
        Currency = product.Currency;
        IsWeighed = product.IsWeighed;
    }

    public decimal LineTotal => decimal.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);

    public string QuantityDisplay => IsWeighed ? $"{Quantity:0.###} kg" : $"{Quantity:0.###}";
    public string UnitPriceDisplay => $"{Currency} {UnitPrice:0.00}";
    public string LineTotalDisplay => $"{Currency} {LineTotal:0.00}";
}
