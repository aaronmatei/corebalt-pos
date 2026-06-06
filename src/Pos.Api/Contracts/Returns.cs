using Pos.Domain.Sales;

namespace Pos.Api.Contracts;

/// <summary>
/// A return / void request. <see cref="ReturnId"/> is a CLIENT-generated UUIDv7 (offline-replay
/// idempotency, like checkout). A full-quantity return is a void. The refund amount is the value of
/// the returned goods; only the method is chosen here.
/// </summary>
public sealed record CreateReturnRequest(
    Guid ReturnId,
    ReturnReason Reason,
    IReadOnlyList<ReturnLineRequest> Lines,
    RefundMethod RefundMethod);

public sealed record ReturnLineRequest(Guid ProductId, decimal Quantity);

public sealed record ReturnResponse(
    Guid ReturnId,
    string ReturnNumber,
    decimal RefundAmount,
    string RefundStatus,
    ReceiptResponse Receipt);
