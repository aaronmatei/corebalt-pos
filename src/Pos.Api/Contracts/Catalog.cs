using Pos.Domain.Catalog;

namespace Pos.Api.Contracts;

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    decimal PriceAmount,
    string PriceCurrency,
    UnitOfMeasure UnitOfMeasure = UnitOfMeasure.Each,
    string? Barcode = null);

public sealed record RepriceProductRequest(decimal Amount, string Currency);

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    MoneyDto Price,
    UnitOfMeasure UnitOfMeasure,
    bool IsActive,
    string? Barcode);
