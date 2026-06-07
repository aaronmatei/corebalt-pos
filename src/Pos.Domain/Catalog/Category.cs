using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Catalog;

/// <summary>
/// Tenant-scoped catalogue grouping (e.g. "Produce", "Beverages", "Services"). Master data like
/// <see cref="Product"/>, but owned at the TENANT level (shared across branches) rather than per-store —
/// the future HQ central catalogue (roadmap M2) keeps one category tree for the chain.
///
/// <para><see cref="ParentId"/> is nullable so the tree is FLAT today (every category a root) yet
/// hierarchy-ready: a sub-category model drops in later with no migration. Uniqueness is on
/// (TenantId, ParentId, Name) so two roots can't share a name and two siblings can't either.</para>
/// </summary>
public sealed class Category : AggregateRoot, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Parent category for a future hierarchy. Null = a root (the only shape used today).</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Sort hint for till browsing / back-office lists. Lower shows first; ties break by name.</summary>
    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    private Category() { } // EF

    public static Category Create(Guid tenantId, string name, Guid? parentId = null, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        return new Category
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            ParentId = parentId,
            DisplayOrder = displayOrder,
            IsActive = true
        };
    }

    /// <summary>Edit the category's display details (name, parent, sort order).</summary>
    public void Update(string name, Guid? parentId, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (parentId == Id) throw new InvalidOperationException("A category cannot be its own parent.");
        Name = name.Trim();
        ParentId = parentId;
        DisplayOrder = displayOrder;
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
