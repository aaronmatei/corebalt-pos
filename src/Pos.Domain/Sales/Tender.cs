using Pos.SharedKernel;

namespace Pos.Domain.Sales;

/// <summary>
/// One payment against a sale. M-Pesa is ASYNCHRONOUS: an STK-push tender is created
/// <see cref="TenderStatus.Pending"/> and is confirmed later (by polling the STK query or by the
/// Daraja callback), so a tender carries a <see cref="Status"/> and a <see cref="ProviderReference"/>
/// (the Daraja CheckoutRequestID used to reconcile). <see cref="Reference"/> holds the human-facing
/// receipt (the M-Pesa confirmation code) once known.
/// </summary>
public sealed class Tender : Entity
{
    public TenderType Type { get; private set; }
    public Money Amount { get; private set; }
    public TenderStatus Status { get; private set; }
    public string? Reference { get; private set; }          // M-Pesa receipt / manual confirmation code
    public string? ProviderReference { get; private set; }  // correlation id (Daraja CheckoutRequestID)

    private Tender() { Amount = Money.Zero(); } // EF

    internal Tender(Guid id, TenderType type, Money amount, TenderStatus status,
        string? reference = null, string? providerReference = null) : base(id)
    {
        Type = type;
        Amount = amount;
        Status = status;
        Reference = reference;
        ProviderReference = providerReference;
    }

    public bool IsPending   => Status == TenderStatus.Pending;
    public bool IsConfirmed => Status == TenderStatus.Confirmed;
    public bool IsFailed    => Status == TenderStatus.Failed;

    internal void SetProviderReference(string providerReference) => ProviderReference = providerReference;

    /// <summary>Confirm a pending tender. Idempotent; a failed tender cannot be confirmed.</summary>
    internal void Confirm(string? reference = null)
    {
        if (Status == TenderStatus.Failed)
            throw new InvalidOperationException("A failed tender cannot be confirmed.");
        Status = TenderStatus.Confirmed;
        if (!string.IsNullOrWhiteSpace(reference)) Reference = reference;
    }

    /// <summary>Fail a pending tender. Idempotent; a confirmed tender cannot be failed.</summary>
    internal void Fail()
    {
        if (Status == TenderStatus.Confirmed)
            throw new InvalidOperationException("A confirmed tender cannot be failed.");
        Status = TenderStatus.Failed;
    }
}
