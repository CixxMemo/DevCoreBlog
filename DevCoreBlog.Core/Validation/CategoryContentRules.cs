using DevCoreBlog.Core.Entities;

namespace DevCoreBlog.Core.Validation;

/// <summary>
/// Defines and evaluates the provider-independent rules for editable categories.
/// </summary>
public static class CategoryContentRules
{
    public const int MaximumNameLength = 100;

    public static void Normalize(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);
        category.Name = category.Name?.Trim() ?? string.Empty;
    }

    public static ContentValidationResult Validate(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        var errors = new List<ContentValidationError>();
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            errors.Add(new(nameof(Category.Name), "Category name is required."));
        }
        else if (category.Name.Length > MaximumNameLength)
        {
            errors.Add(new(
                nameof(Category.Name),
                $"Category name cannot exceed {MaximumNameLength} characters."));
        }

        return ContentValidationResult.FromErrors(errors);
    }
}
