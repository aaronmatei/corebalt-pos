using Pos.Domain.Catalog;

namespace Pos.Api.Contracts;

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    decimal PriceAmount,
    string PriceCurrency,
    UnitOfMeasure UnitOfMeasure = UnitOfMeasure.Each,
    string? Barcode = null,
    TaxClass TaxClass = TaxClass.StandardRated);

public sealed record UpdateProductRequest(
    string Name,
    string? Barcode,
    UnitOfMeasure UnitOfMeasure,
    TaxClass TaxClass,
    bool IsActive);

public sealed record RepriceProductRequest(decimal Amount, string Currency);

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    MoneyDto Price,
    UnitOfMeasure UnitOfMeasure,
    bool IsActive,
    string? Barcode,
    TaxClass TaxClass);
