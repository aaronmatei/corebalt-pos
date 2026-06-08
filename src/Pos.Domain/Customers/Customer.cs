using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Customers;

/// <summary>
/// A customer / loyalty member. Tenant-scoped master data (like <see cref="Pos.Domain.Catalog.Category"/>):
/// shared across the chain's branches, so a member earns and is recognised at any store (M2-ready). A sale
/// references a customer by id (loose ref, no navigation); null = a walk-in (the default — selling never
/// requires a customer). Loyalty points are a running balance mutated only through the methods here.
///
/// <para>Eventless on purpose: tenant-scoped aggregates don't go through the (tenant+store) outbox; like
/// Category, accruals are local master-data changes, not store facts to ship to HQ.</para>
/// </summary>
public sealed class Customer : AggregateRoot, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }       // normalized (2547########) when Kenyan
    public string? Email { get; private set; }
    public string? KraPin { get; private set; }      // optional KYC (e.g. for a VAT customer)
    public string? NationalId { get; private set; }
    public int LoyaltyPoints { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Customer() { } // EF

    public static Customer Create(Guid tenantId, string name, string? phone = null, string? email = null,
        string? kraPin = null, string? nationalId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        Validate(kraPin, nationalId);

        return new Customer
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Phone = NormalizePhone(phone),
            Email = Blank(email),
            KraPin = Blank(kraPin)?.ToUpperInvariant(),
            NationalId = Blank(nationalId),
            LoyaltyPoints = 0,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateContact(string name, string? phone, string? email, string? kraPin, string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        Validate(kraPin, nationalId);
        Name = name.Trim();
        Phone = NormalizePhone(phone);
        Email = Blank(email);
        KraPin = Blank(kraPin)?.ToUpperInvariant();
        NationalId = Blank(nationalId);
    }

    /// <summary>Add earned loyalty points (e.g. on a completed sale). Ignores a non-positive amount.</summary>
    public void AccruePoints(int points)
    {
        if (points <= 0) return;
        LoyaltyPoints += points;
    }

    /// <summary>Spend loyalty points. Refuses to overdraw the balance.</summary>
    public void RedeemPoints(int points)
    {
        if (points <= 0) throw new ArgumentException("Redeemed points must be positive.", nameof(points));
        if (points > LoyaltyPoints) throw new InvalidOperationException("Insufficient loyalty points.");
        LoyaltyPoints -= points;
    }

    /// <summary>Manual correction by a manager (positive or negative); never drives the balance below zero.</summary>
    public void AdjustPoints(int delta)
    {
        LoyaltyPoints = Math.Max(0, LoyaltyPoints + delta);
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    private static void Validate(string? kraPin, string? nationalId)
    {
        if (!string.IsNullOrWhiteSpace(kraPin) && !KenyanIdValidator.IsValidKraPin(kraPin))
            throw new ArgumentException("KRA PIN format is invalid (e.g. A001234567Z).", nameof(kraPin));
        if (!string.IsNullOrWhiteSpace(nationalId) && !KenyanIdValidator.IsValidNationalId(nationalId))
            throw new ArgumentException("National ID must be 6–10 digits.", nameof(nationalId));
    }

    private static string? NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : KenyanIdValidator.NormalizePhone(phone);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
