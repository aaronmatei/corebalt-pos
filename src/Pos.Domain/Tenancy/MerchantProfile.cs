using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Tenancy;

/// <summary>
/// The CLIENT (retailer) we installed for — their own legal identity, branches and branding. One per
/// tenant (single tenant per on-prem install). This is what the receipt header reads — never Corebalt's
/// identity. Corebalt is the VENDOR; an optional "Powered by Corebalt POS" footer is the only place
/// the vendor appears.
/// </summary>
public sealed class MerchantProfile : Entity, ITenantScoped
{
    private readonly List<Branch> _branches = new();

    public Guid TenantId { get; private set; }
    public string LegalName { get; private set; } = string.Empty;
    public string TradingName { get; private set; } = string.Empty;
    public string KraPin { get; private set; } = string.Empty;
    public bool VatRegistered { get; private set; }
    public string? VatNumber { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "KES";
    public string? LogoUrl { get; private set; }
    public string? ReceiptFooter { get; private set; }
    public bool ShowPoweredBy { get; private set; } = true;
    public bool SetupComplete { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<Branch> Branches => _branches.AsReadOnly();

    private MerchantProfile() { } // EF

    public static MerchantProfile Create(Guid tenantId, string legalName, string? tradingName, string kraPin,
        bool vatRegistered, string? vatNumber, string phone, string? email, string address, string currency)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(legalName)) throw new ArgumentException("Legal name is required.", nameof(legalName));
        if (string.IsNullOrWhiteSpace(kraPin)) throw new ArgumentException("KRA PIN is required.", nameof(kraPin));

        return new MerchantProfile
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            LegalName = legalName.Trim(),
            TradingName = string.IsNullOrWhiteSpace(tradingName) ? legalName.Trim() : tradingName.Trim(),
            KraPin = kraPin.Trim(),
            VatRegistered = vatRegistered,
            VatNumber = vatRegistered ? vatNumber?.Trim() : null,
            Phone = (phone ?? "").Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Address = (address ?? "").Trim(),
            Currency = string.IsNullOrWhiteSpace(currency) ? "KES" : currency.Trim().ToUpperInvariant(),
            ShowPoweredBy = true,
            SetupComplete = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Add a branch. <paramref name="id"/> is the StoreId this branch trades under (the till's store scope).</summary>
    public void AddBranch(Guid id, string name, string code, string address)
    {
        if (id == Guid.Empty) throw new ArgumentException("Branch id (StoreId) is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Branch name is required.", nameof(name));
        _branches.Add(new Branch(id, name.Trim(), (code ?? "").Trim(), (address ?? "").Trim()));
    }

    public void SetBranding(string? logoUrl, string? receiptFooter, bool showPoweredBy)
    {
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        ReceiptFooter = string.IsNullOrWhiteSpace(receiptFooter) ? null : receiptFooter.Trim();
        ShowPoweredBy = showPoweredBy;
    }

    public void MarkSetupComplete() => SetupComplete = true;

    public Branch? BranchFor(Guid storeId) => _branches.FirstOrDefault(b => b.Id == storeId) ?? _branches.FirstOrDefault();
}

public sealed class Branch : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;

    private Branch() { } // EF
    internal Branch(Guid id, string name, string code, string address) : base(id)
    {
        Name = name;
        Code = code;
        Address = address;
    }
}
