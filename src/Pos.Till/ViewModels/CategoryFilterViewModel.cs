using Pos.Till.Api;

namespace Pos.Till.ViewModels;

/// <summary>
/// One option in the catalogue's category filter. Two synthetic rows bracket the real categories:
/// <see cref="All"/> (no filter) and <see cref="Uncategorized"/> (products with no category — handy for
/// produce / weighed goods / services that aren't barcoded). Real rows wrap a <see cref="CategoryDto"/>.
/// </summary>
public sealed class CategoryFilterViewModel
{
    public string Name { get; }
    public Guid? CategoryId { get; }
    public bool IsAll { get; }

    private CategoryFilterViewModel(string name, Guid? categoryId, bool isAll)
    {
        Name = name;
        CategoryId = categoryId;
        IsAll = isAll;
    }

    public static CategoryFilterViewModel All { get; } = new("All categories", null, isAll: true);
    public static CategoryFilterViewModel Uncategorized { get; } = new("Uncategorized", null, isAll: false);
    public static CategoryFilterViewModel For(CategoryDto c) => new(c.Name, c.Id, isAll: false);

    /// <summary>Does a catalogue row pass this filter? "All" matches everything; otherwise match the id
    /// (Uncategorized's null id matches products whose CategoryId is null).</summary>
    public bool Matches(ProductRowViewModel p) => IsAll || p.CategoryId == CategoryId;
}
