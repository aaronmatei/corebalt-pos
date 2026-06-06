namespace Pos.Api.Contracts;

public sealed record CreateBranchRequest(string Name, string Code, string Address);
public sealed record BranchResponse(Guid Id, string Name, string Code, string Address);
public sealed record EntitlementsResponse(string Edition, string[] Features, int MaxTills, int MaxBranches, DateTimeOffset? ValidUntil, bool Expired);
public sealed record MerchantProfileResponse(string LegalName, string TradingName, string KraPin, bool VatRegistered, string? VatNumber, string Currency, bool SetupComplete);
