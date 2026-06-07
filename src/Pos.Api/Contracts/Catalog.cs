using Pos.Domain.Catalog;

namespace Pos.Api.Contracts;

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    decimal PriceAmount,
    string PriceCurrency,
    UnitOfMeasure UnitOfMeasure = UnitOfMeasure.Each,
    string? Barcode = null,
    TaxClass TaxClass = TaxClass.StandardRated,
    Guid? CategoryId = null);

public sealed record UpdateProductRequest(
    string Name,
    string? Barcode,
    UnitOfMeasure UnitOfMeasure,
    TaxClass TaxClass,
    bool IsActive,
    Guid? CategoryId = null,
    decimal? ReorderLevel = null,
    decimal? ReorderQuantity = null);

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
    TaxClass TaxClass,
    Guid? CategoryId,
    decimal? ReorderLevel,
    decimal? ReorderQuantity);

// ── Categories (tenant-scoped master data) ──
public sealed record CreateCategoryRequest(string Name, Guid? ParentId = null, int DisplayOrder = 0);

public sealed record UpdateCategoryRequest(string Name, Guid? ParentId, int DisplayOrder, bool IsActive);

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int DisplayOrder,
    bool IsActive);
