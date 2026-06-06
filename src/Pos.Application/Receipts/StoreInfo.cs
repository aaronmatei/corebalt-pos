namespace Pos.Application.Receipts;

/// <summary>
/// Receipt header identity for the branch, sourced from the host's "Store" config section. Held as a
/// plain POCO (no IConfiguration dependency in Application); the API composition root populates it.
/// </summary>
public sealed record StoreInfo(
    string LegalName,
    string KraPin,
    string BranchName,
    string BranchAddress,
    string Phone,
    string VatNumber,
    string Currency);
