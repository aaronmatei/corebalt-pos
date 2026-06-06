using Pos.Application.Payments;

namespace Pos.Api.Tests;

/// <summary>
/// In-memory <see cref="IMpesaClient"/> so the M-Pesa integration tests never touch the network or
/// need Daraja credentials. Tests set the knobs (push success/failure, query outcome) before
/// driving the flow, then assert on the recorded calls. Registered as a singleton, so reset it at
/// the start of each test.
/// </summary>
public sealed class FakeMpesaClient : IMpesaClient
{
    // Knobs
    public bool PushShouldFail { get; set; }
    public MpesaQueryState QueryState { get; set; } = MpesaQueryState.Success;
    public int QueryResultCode { get; set; }
    public string? QueryResultDesc { get; set; } = "The service request is processed successfully.";
    public string? Receipt { get; set; } = "FAKE12RECEIPT";

    // Recorded activity
    public StkPushRequest? LastPush { get; private set; }
    public string? LastCheckoutRequestId { get; private set; }
    public int PushCount { get; private set; }
    public int QueryCount { get; private set; }

    public void Reset()
    {
        PushShouldFail = false;
        QueryState = MpesaQueryState.Success;
        QueryResultCode = 0;
        QueryResultDesc = "The service request is processed successfully.";
        Receipt = "FAKE12RECEIPT";
        LastPush = null;
        LastCheckoutRequestId = null;
        PushCount = 0;
        QueryCount = 0;
    }

    public Task<StkPushResult> StkPushAsync(StkPushRequest request, CancellationToken ct = default)
    {
        PushCount++;
        LastPush = request;
        if (PushShouldFail)
            return Task.FromResult(new StkPushResult(false, null, null, "1", "Push rejected", "Push rejected by Daraja."));

        LastCheckoutRequestId = "ws_CO_" + Guid.NewGuid().ToString("N")[..16];
        return Task.FromResult(new StkPushResult(
            true, LastCheckoutRequestId, "mr_" + Guid.NewGuid().ToString("N")[..10],
            "0", "Success. Request accepted for processing", null));
    }

    public Task<StkQueryResult> StkQueryAsync(string checkoutRequestId, CancellationToken ct = default)
    {
        QueryCount++;
        var receipt = QueryState == MpesaQueryState.Success ? Receipt : null;
        return Task.FromResult(new StkQueryResult(QueryState, QueryResultCode, QueryResultDesc, receipt));
    }
}
