namespace Pos.Application.Sync;

/// <summary>
/// HQ→store catalogue pull (M2). A store calls <c>GET /hq/catalog/changes?since={cursor}</c> and applies
/// each item by SKU. <see cref="Cursor"/> is the highest <c>Seq</c> in this page — the store persists it
/// and passes it back next time. <see cref="HasMore"/> tells it to keep draining.
/// </summary>
public sealed record CatalogPullResponse(IReadOnlyList<CatalogItemDto> Items, long Cursor, bool HasMore);

public sealed record CatalogItemDto(
    long Seq,
    Guid CatalogItemId,
    string Sku,
    string Name,
    decimal PriceAmount,
    string Currency,
    string TaxClass,
    string UnitOfMeasure,
    string? Barcode,
    bool IsActive);
