using Pos.Domain.Tenancy;

namespace Pos.Application.Receipts;

/// <summary>
/// Receipt header identity for a branch — the CLIENT's identity, projected from their DB-backed
/// <see cref="MerchantProfile"/> (never Corebalt's, never appsettings). <see cref="ShowPoweredBy"/>
/// drives the optional vendor footer line.
/// </summary>
public sealed record StoreInfo(
    string LegalName,
    string KraPin,
    string BranchName,
    string BranchAddress,
    string Phone,
    string VatNumber,
    string Currency,
    string? Footer = null,
    bool ShowPoweredBy = false)
{
    public static StoreInfo From(MerchantProfile profile, Guid storeId)
    {
        var branch = profile.BranchFor(storeId);
        return new StoreInfo(
            LegalName: profile.LegalName,
            KraPin: profile.KraPin,
            BranchName: branch?.Name ?? profile.TradingName,
            BranchAddress: branch?.Address ?? profile.Address,
            Phone: profile.Phone,
            VatNumber: profile.VatRegistered ? (profile.VatNumber ?? "") : "Not VAT registered",
            Currency: profile.Currency,
            Footer: profile.ReceiptFooter,
            ShowPoweredBy: profile.ShowPoweredBy);
    }

    /// <summary>Neutral placeholder for an un-provisioned install — NEVER the vendor's identity.</summary>
    public static StoreInfo Unconfigured(string currency = "KES") =>
        new("(store not configured)", "", "", "", "", "", currency);
}
