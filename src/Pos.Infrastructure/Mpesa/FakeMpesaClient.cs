using Microsoft.Extensions.Logging;
using Pos.Application.Payments;

namespace Pos.Infrastructure.Mpesa;

/// <summary>
/// In-memory M-Pesa client for DEV/DEMO only (Mpesa:UseFake=true; never honored in Production). No
/// Daraja, no real device: StkPush is accepted and StkQuery immediately reports success, so the till's
/// "Pay with M-Pesa" flow completes on its own (initiate → poll → Confirmed → sale completes). The
/// CheckoutRequestID + receipt are fabricated and clearly fake.
/// </summary>
public sealed class FakeMpesaClient : IMpesaClient
{
    private readonly ILogger<FakeMpesaClient> _log;
    public FakeMpesaClient(ILogger<FakeMpesaClient> log) => _log = log;

    public Task<StkPushResult> StkPushAsync(StkPushRequest request, CancellationToken ct = default)
    {
        var cri = "fake_" + Guid.NewGuid().ToString("N")[..16];
        _log.LogWarning("FAKE M-Pesa (dev) StkPush amount={Amount} -> {Cri} (NOT real Daraja)", request.Amount, cri);
        return Task.FromResult(new StkPushResult(
            true, cri, "fake_mr", "0", "Success. Request accepted for processing", null));
    }

    public Task<StkQueryResult> StkQueryAsync(string checkoutRequestId, CancellationToken ct = default)
    {
        // Auto-confirm: the demo never enters a PIN, so the first poll succeeds.
        var receipt = ("SBX" + checkoutRequestId.Replace("fake_", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant());
        if (receipt.Length > 12) receipt = receipt[..12];
        _log.LogWarning("FAKE M-Pesa (dev) StkQuery {Cri} -> Success (NOT real Daraja)", checkoutRequestId);
        return Task.FromResult(new StkQueryResult(
            MpesaQueryState.Success, 0, "The service request is processed successfully.", receipt));
    }
}
