namespace Pos.Application.Integration;

/// <summary>
/// A completed sale in the shape Corebalt's POS sale-webhook expects (camelCase on the wire via the
/// web JSON defaults). <see cref="PosSaleId"/> is the idempotency key — Corebalt ignores a re-send.
/// Lines carry the SKU (the two catalogues share SKUs); quantity is whole units (Corebalt inventory
/// is integer-unit — see the forwarder for the weighed-goods caveat).
/// </summary>
public sealed record ErpSaleDto(
    string PosSaleId,
    IReadOnlyList<ErpSaleLineDto> Items,
    decimal Total,
    string PaymentMethod,
    DateTimeOffset OccurredAt,
    string? CustomerRef);

public sealed record ErpSaleLineDto(string Sku, int Qty, decimal UnitPrice);
