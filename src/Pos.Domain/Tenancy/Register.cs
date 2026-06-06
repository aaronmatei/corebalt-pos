using Pos.SharedKernel;

namespace Pos.Domain.Tenancy;

/// <summary>
/// A till/lane within a store. Carries a human Number + Name (e.g. "1" / "Lane 1") that the receipt
/// prints, while the UUIDv7 <see cref="Entity.Id"/> stays the internal id the till and the
/// per-register <see cref="PrinterProfile"/> key off. Store-authoritative (tenant + store scoped).
/// </summary>
public sealed class Register : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public string Number { get; private set; } = "";
    public string Name { get; private set; } = "";

    /// <summary>What the receipt shows for "Till:" — the Name, falling back to the Number.</summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? Number : Name;

    private Register() { } // EF

    /// <summary>Create a lane. The id is the till's own UUIDv7 (the RegisterId it already sends).</summary>
    public static Register Create(Guid tenantId, Guid storeId, Guid id, string number, string name) => new()
    {
        Id = id,
        TenantId = tenantId,
        StoreId = storeId,
        Number = number?.Trim() ?? "",
        Name = string.IsNullOrWhiteSpace(name) ? $"Lane {number}" : name.Trim(),
    };

    public void Rename(string number, string name)
    {
        Number = number?.Trim() ?? Number;
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
    }
}
