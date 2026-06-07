using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Contracts;
using Pos.Domain.Identity;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Optional fingerprint sign-in (PIN stays the fallback). A manager enrols a cashier's template under
/// consent; a matching probe issues the SAME JWT as PIN and resolves to that cashier; a non-matching or
/// removed template is rejected; enrolment without consent is refused. Uses REAL JWTs against the
/// bootstrap manager (the dev stub authenticator matches templates by exact bytes).
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class FingerprintAuthTests(PosApiFixture fx)
{
    private const string BootstrapUser = "manager";
    private const string BootstrapPassword = "ChangeMe!123";

    [Fact]
    public async Task Enrolled_fingerprint_signs_in_with_the_same_jwt_and_pin_still_works()
    {
        var manager = await ManagerTokenAsync();
        var (userId, staff, pin) = await CreateCashierAsync(manager, "Fingo Cashier");

        // Manager enrols a fingerprint, under consent.
        var enroll = await Bearer(manager).PostAsJsonAsync($"/api/v1/users/{userId}/fingerprints",
            new EnrollFingerprintRequest(DevProbe(staff), "Right thumb", Consent: true), PosApiFixture.Json);
        enroll.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Sign in by fingerprint → a usable token resolved to THIS cashier.
        var login = await Anon().PostAsJsonAsync("/api/v1/auth/fingerprint-login",
            new FingerprintLoginRequest(DevProbe(staff)), PosApiFixture.Json);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<FingerprintLoginResponse>(PosApiFixture.Json))!;
        token.AccessToken.Should().NotBeNullOrWhiteSpace();
        token.StaffCode.Should().Be(staff);
        token.Name.Should().Be("Fingo Cashier");

        // The token actually works (it's the same JWT a PIN login would mint).
        (await Bearer(token.AccessToken).GetAsync("/api/v1/products")).StatusCode.Should().Be(HttpStatusCode.OK);

        // PIN remains a valid fallback for the same cashier.
        (await Anon().PostAsJsonAsync("/api/v1/auth/pin-login", new PinLoginRequest(staff, pin), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enrolment_without_consent_is_refused()
    {
        var manager = await ManagerTokenAsync();
        var (userId, staff, _) = await CreateCashierAsync(manager, "No Consent");

        var resp = await Bearer(manager).PostAsJsonAsync($"/api/v1/users/{userId}/fingerprints",
            new EnrollFingerprintRequest(DevProbe(staff), null, Consent: false), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict); // domain rule: consent required
    }

    [Fact]
    public async Task An_unknown_fingerprint_is_rejected()
    {
        // No enrolment for this random probe → 401, regardless of any other enrolments.
        var probe = Convert.ToBase64String(Encoding.UTF8.GetBytes("STUB:" + Guid.NewGuid().ToString("N")));
        var login = await Anon().PostAsJsonAsync("/api/v1/auth/fingerprint-login",
            new FingerprintLoginRequest(probe), PosApiFixture.Json);
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Removing_an_enrolled_fingerprint_stops_it_matching()
    {
        var manager = await ManagerTokenAsync();
        var (userId, staff, _) = await CreateCashierAsync(manager, "Temp Print");

        await Bearer(manager).PostAsJsonAsync($"/api/v1/users/{userId}/fingerprints",
            new EnrollFingerprintRequest(DevProbe(staff), null, Consent: true), PosApiFixture.Json);

        // Matches while enrolled.
        (await Anon().PostAsJsonAsync("/api/v1/auth/fingerprint-login", new FingerprintLoginRequest(DevProbe(staff)), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Find the enrolled id and remove it.
        var list = (await (await Bearer(manager).GetAsync($"/api/v1/users/{userId}/fingerprints"))
            .Content.ReadFromJsonAsync<List<FingerprintResponse>>(PosApiFixture.Json))!;
        list.Should().ContainSingle();
        var del = await Bearer(manager).DeleteAsync($"/api/v1/users/{userId}/fingerprints/{list[0].Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // No longer matches.
        (await Anon().PostAsJsonAsync("/api/v1/auth/fingerprint-login", new FingerprintLoginRequest(DevProbe(staff)), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── helpers ──
    private static string DevProbe(string staff) => Convert.ToBase64String(Encoding.UTF8.GetBytes("STUB:" + staff));

    private HttpClient Anon() => fx.Factory.CreateClient();

    private HttpClient Bearer(string token)
    {
        var c = fx.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    private async Task<string> ManagerTokenAsync()
    {
        var resp = await Anon().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(BootstrapUser, BootstrapPassword), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!.AccessToken;
    }

    private async Task<(Guid Id, string Staff, string Pin)> CreateCashierAsync(string managerToken, string name)
    {
        var staff = $"F{Guid.NewGuid():N}"[..8];
        const string pin = "1234";
        var resp = await Bearer(managerToken).PostAsJsonAsync("/api/v1/users", new CreateUserRequest(
            Name: name, Username: $"u-{Guid.NewGuid():N}"[..12], StaffCode: staff, Role: UserRole.Cashier,
            Pin: pin, Password: null), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        var user = (await resp.Content.ReadFromJsonAsync<UserResponse>(PosApiFixture.Json))!;
        return (user.Id, staff, pin);
    }
}
