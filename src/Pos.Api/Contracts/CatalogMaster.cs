using Pos.Domain.Catalog;

namespace Pos.Api.Contracts;

// HQ catalogue master (M2). Reuses MoneyDto / RepriceProductRequest from Catalog.cs.
public sealed record CreateCatalogItemRequest(
    string Sku,
    string Name,
    decimal PriceAmount,
    string PriceCurrency,
    UnitOfMeasure UnitOfMeasure = UnitOfMeasure.Each,
    string? Barcode = null,
    TaxClass TaxClass = TaxClass.StandardRated,
    string? CategoryName = null);

public sealed record UpdateCatalogItemRequest(
    string Name,
    string? Barcode,
    UnitOfMeasure UnitOfMeasure,
    TaxClass TaxClass,
    string? CategoryName = null);

public sealed record SetCatalogActiveRequest(bool Active);

public sealed record CatalogItemResponse(
    Guid Id,
    string Sku,
    string Name,
    MoneyDto Price,
    UnitOfMeasure UnitOfMeasure,
    TaxClass TaxClass,
    string? Barcode,
    bool IsActive,
    DateTimeOffset UpdatedAtUtc,
    string? CategoryName = null);
