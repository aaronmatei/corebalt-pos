using Pos.SharedKernel;

namespace Pos.Domain.Sales.Events;

/// <summary>
/// Raised when a credit note (return / void) is issued. Drained to the outbox for audit + the future
/// HQ sync. The reversal is a NEW immutable fact referencing the original sale — never a mutation.
/// </summary>
public sealed record CreditNoteIssued(
    Guid CreditNoteId,
    Guid TenantId,
    Guid StoreId,
    Guid OriginalSaleId,
    decimal RefundTotal,
    string Reason) : DomainEvent;
