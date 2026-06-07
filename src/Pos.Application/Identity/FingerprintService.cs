using Pos.Application.Abstractions;
using Pos.Domain.Identity;

namespace Pos.Application.Identity;

/// <summary>Result of a fingerprint sign-in: the SAME JWT the PIN flow issues, plus the resolved
/// cashier label so the till can show who signed in (it doesn't decode the token).</summary>
public sealed record FingerprintLoginResult(AccessToken Token, Guid UserId, string StaffCode, string Name);

/// <summary>Metadata for an enrolled fingerprint (never the template itself).</summary>
public sealed record FingerprintInfo(Guid Id, string? FingerLabel, DateTimeOffset EnrolledAtUtc, bool ConsentGiven);

/// <summary>
/// Fingerprint enrolment + sign-in for the store-server's tenant/store. Sign-in IDENTIFIES a probe against
/// the enrolled templates (locally, via the reader SDK) and, on a match, issues the SAME JWT as PIN login —
/// there is no separate identity store. PIN remains the fallback at all times. Enrolment is a supervised,
/// consented act performed by a manager. Scopes to <see cref="StoreServerOptions"/> like <see cref="AuthService"/>.
/// </summary>
public sealed class FingerprintService
{
    private readonly IUserRepository _users;
    private readonly IFingerprintAuthenticator _authenticator;
    private readonly ITokenIssuer _tokens;
    private readonly StoreServerOptions _server;
    private readonly ICurrentContext _ctx;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public FingerprintService(IUserRepository users, IFingerprintAuthenticator authenticator, ITokenIssuer tokens,
        StoreServerOptions server, ICurrentContext ctx, IClock clock, IUnitOfWork uow)
    {
        _users = users;
        _authenticator = authenticator;
        _tokens = tokens;
        _server = server;
        _ctx = ctx;
        _clock = clock;
        _uow = uow;
    }

    public bool IsEnabled => _authenticator.IsEnabled;

    /// <summary>
    /// Manager action, under supervision: enrol a fingerprint for a cashier. <paramref name="capturedSample"/>
    /// comes from the enrolment station's reader (the raw image already discarded). REFUSES without explicit
    /// consent. Returns false if the target user isn't found. The enrolling manager is recorded for audit.
    /// </summary>
    public async Task<bool> EnrollAsync(Guid targetUserId, byte[] capturedSample, string? fingerLabel,
        bool consentGiven, CancellationToken ct = default)
    {
        if (!_authenticator.IsEnabled)
            throw new InvalidOperationException("Fingerprint authentication is not enabled on this install.");
        if (!consentGiven)
            throw new InvalidOperationException("Explicit consent is required to enrol a fingerprint.");

        var user = await _users.GetByIdWithFingerprintsAsync(_server.TenantId, _server.StoreId, targetUserId, ct);
        if (user is null) return false;

        var template = _authenticator.ExtractTemplate(capturedSample);
        var credential = user.EnrollFingerprint(template, fingerLabel, enrolledByUserId: _ctx.UserId, consentGiven: true, now: _clock.UtcNow);
        await _users.AddFingerprintAsync(credential, ct); // explicit Added — attaches to an existing user
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Identify a probe captured at the till against the store's enrolled templates (LOCAL 1:N match) and,
    /// on a confident match to an active user, issue a JWT identical to PIN login. Null on no match / disabled
    /// — the caller maps that to a single 401, and the cashier falls back to PIN.
    /// </summary>
    public async Task<FingerprintLoginResult?> LoginAsync(byte[] probeSample, CancellationToken ct = default)
    {
        if (!_authenticator.IsEnabled || probeSample is null || probeSample.Length == 0) return null;

        var users = await _users.ListActiveWithFingerprintsAsync(_server.TenantId, _server.StoreId, ct);
        var candidates = users
            .SelectMany(u => u.Fingerprints.Select(f => new FingerprintCandidate(u.Id, f.TemplateBytes)))
            .ToList();
        if (candidates.Count == 0) return null;

        var matchedUserId = _authenticator.Identify(probeSample, candidates);
        if (matchedUserId is null) return null;

        var user = users.FirstOrDefault(u => u.Id == matchedUserId.Value && u.IsActive);
        if (user is null) return null;

        return new FingerprintLoginResult(_tokens.Issue(user), user.Id, user.StaffCode, user.Name);
    }

    public async Task<IReadOnlyList<FingerprintInfo>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithFingerprintsAsync(_server.TenantId, _server.StoreId, userId, ct);
        return user is null
            ? []
            : user.Fingerprints
                .Select(f => new FingerprintInfo(f.Id, f.FingerLabel, f.EnrolledAtUtc, f.ConsentGiven))
                .ToList();
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid fingerprintId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithFingerprintsAsync(_server.TenantId, _server.StoreId, userId, ct);
        var credential = user?.Fingerprints.FirstOrDefault(f => f.Id == fingerprintId);
        if (user is null || credential is null) return false;
        user.RemoveFingerprint(fingerprintId);  // keep the in-memory aggregate consistent
        _users.RemoveFingerprint(credential);    // explicit Deleted
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
