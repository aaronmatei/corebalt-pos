namespace Pos.Application.Payments;

/// <summary>What we send Daraja to trigger the customer's STK (SIM Toolkit) PIN prompt.</summary>
public sealed record StkPushRequest(decimal Amount, string PhoneNumber, string AccountReference, string Description);

/// <summary>
/// Result of initiating an STK push. <see cref="Ok"/> means Daraja ACCEPTED the request (the
/// customer's phone will prompt) — it does NOT mean paid. The CheckoutRequestId is the correlation
/// id we reconcile against later.
/// </summary>
public sealed record StkPushResult(
    bool Ok,
    string? CheckoutRequestId,
    string? MerchantRequestId,
    string? ResponseCode,
    string? ResponseDescription,
    string? Error);

/// <summary>Where an STK push stands: still waiting on the customer, paid, or terminally failed.</summary>
public enum MpesaQueryState { Processing = 0, Success = 1, Failed = 2 }

public sealed record StkQueryResult(
    MpesaQueryState State,
    int ResultCode,
    string? ResultDescription,
    string? MpesaReceipt);

/// <summary>
/// Port for the M-Pesa provider (Daraja). Lives in Application so the domain/use-case code never
/// references Safaricom HTTP details; Infrastructure supplies the real client and the tests a fake.
/// </summary>
public interface IMpesaClient
{
    Task<StkPushResult> StkPushAsync(StkPushRequest request, CancellationToken ct = default);
    Task<StkQueryResult> StkQueryAsync(string checkoutRequestId, CancellationToken ct = default);
}
