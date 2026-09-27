using DevCoreBlog.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DevCoreBlog.Components;

/// <summary>Supplies active categories to the shared public navigation.</summary>
public sealed class PublicCategoryNavigationViewComponent : ViewComponent
{
    private readonly ICategoryService _categories;

    public PublicCategoryNavigationViewComponent(ICategoryService categories)
    {
        _categories = categories;
    }

    public async Task<IViewComponentResult> InvokeAsync() =>
        View(await _categories.GetActiveCategoriesAsync());
}
