using System.Net.Http.Json;
using Pos.Api.Contracts;
using Pos.SharedKernel.Ids;

namespace Pos.Api.Tests;

/// <summary>Test helpers for the register-shift precondition: a register needs an OPEN session before
/// it can sell. Most tests just need "a register with an open shift" — <see cref="OpenShiftAsync()"/>.</summary>
internal static class CashTestExtensions
{
    /// <summary>Open a shift on a fresh register and return its id (use it as the checkout RegisterId).</summary>
    public static async Task<Guid> OpenShiftAsync(this HttpClient client, decimal openingFloat = 2000m)
    {
        var registerId = Uuid7.NewGuid();
        await client.OpenShiftAsync(registerId, openingFloat);
        return registerId;
    }

    /// <summary>Open a shift on a specific register.</summary>
    public static async Task OpenShiftAsync(this HttpClient client, Guid registerId, decimal openingFloat = 2000m)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/sessions/open",
            new OpenSessionRequest(registerId, openingFloat), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
    }
}
