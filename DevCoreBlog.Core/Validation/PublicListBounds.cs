namespace DevCoreBlog.Core.Validation;

/// <summary>Finite reader query limits shared by HTTP, service and persistence boundaries.</summary>
public static class PublicListBounds
{
    public const int MaximumPage = 1000;
    public const int MaximumTermLength = 100;
    public const int MaximumCategoryLength = 200;
    public static IReadOnlyList<int> PageSizes { get; } = Array.AsReadOnly(new[] { 9, 18, 27 });
    public static bool IsValidPageSize(int size) => PageSizes.Contains(size);
    public static void Validate(int page, int pageSize, string? term = null, string? category = null)
    {
        if (page is < 1 or > MaximumPage) throw new ArgumentOutOfRangeException(nameof(page));
        if (!IsValidPageSize(pageSize)) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (term?.Length > MaximumTermLength) throw new ArgumentException("Search term is too long.", nameof(term));
        if (category?.Length > MaximumCategoryLength) throw new ArgumentException("Category is too long.", nameof(category));
    }
}
