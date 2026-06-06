using Pos.Application.Licensing;
using Pos.Domain.Tenancy;

namespace Pos.Api.Tests;

/// <summary>
/// The VENDOR side of licensing for tests: signs licence tokens with Corebalt's private key (the match
/// to the public key embedded in <see cref="LicenseVerifier"/>). In production this key lives only in
/// Corebalt's licence-issuing tool, never in the product.
/// </summary>
internal static class LicenseTestSigner
{
    // Matches the public key in Pos.Application.Licensing.LicenseVerifier (ECDSA P-256, test keypair).
    private const string PrivateKeyPkcs8Base64 =
        "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgiVn348tlcNMJULGeVYADAOie2BfiahhfZDFDIog7jDGhRANCAASpYQEqNby3nRLtHPK44Iktwld5EkYVT5ZTTeYQNXedTiF2X/uwehlhz8umKuhUrw/ttVxwujYrLc0qmOlDP2sG";

    public static string Sign(Guid tenant, Edition edition, Feature features, int maxTills, int maxBranches,
        DateTimeOffset validUntil, DateTimeOffset? issuedAt = null) =>
        LicenseSigner.Sign(
            new License(tenant, edition, features, maxTills, maxBranches, validUntil, issuedAt ?? DateTimeOffset.UtcNow),
            PrivateKeyPkcs8Base64);

    /// <summary>A full Supermarket licence (all common features) valid for a year — the test default.</summary>
    public static string Standard(Guid tenant) =>
        Sign(tenant, Edition.Supermarket, Feature.MultiBranch | Feature.Promotions | Feature.Loyalty, 8, 10,
            DateTimeOffset.UtcNow.AddYears(1));
}
