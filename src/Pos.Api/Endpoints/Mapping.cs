using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel;

namespace Pos.Api.Endpoints;

internal static class Mapping
{
    public static MoneyDto ToDto(this Money m) => new(m.Amount, m.Currency);

    public static ProductResponse ToResponse(this Product p) =>
        new(p.Id, p.Sku, p.Name, p.Price.ToDto(), p.UnitOfMeasure, p.IsActive, p.Barcode, p.TaxClass, p.CategoryId,
            p.ReorderLevel, p.ReorderQuantity);

    public static CategoryResponse ToResponse(this Category c) =>
        new(c.Id, c.Name, c.ParentId, c.DisplayOrder, c.IsActive);

    public static SaleResponse ToResponse(this Sale s) => new(
        s.Id,
        s.Status,
        s.Currency,
        s.Lines.Select(l => new SaleLineResponse(
            l.Id, l.ProductId, l.Description, l.Quantity,
            l.UnitPrice.ToDto(), l.LineTotal.ToDto())).ToList(),
        s.Tenders.Select(t => new TenderResponse(
            t.Id, t.Type, t.Status, t.Amount.ToDto(), t.Reference, t.ProviderReference)).ToList(),
        s.Subtotal.ToDto(),
        s.Paid.ToDto(),
        s.BalanceDue.ToDto(),
        s.CompletedAtUtc);
}
