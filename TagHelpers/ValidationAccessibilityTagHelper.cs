using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DevCoreBlog.TagHelpers;

/// <summary>Connects server field errors to controls, including controls with a custom HTML ID.</summary>
[HtmlTargetElement("input", Attributes = "asp-for")]
[HtmlTargetElement("textarea", Attributes = "asp-for")]
[HtmlTargetElement("select", Attributes = "asp-for")]
[HtmlTargetElement("span", Attributes = "asp-validation-for")]
public sealed class ValidationAccessibilityTagHelper : TagHelper
{
    [HtmlAttributeName("asp-for")] public ModelExpression? For { get; set; }
    [HtmlAttributeName("asp-validation-for")] public ModelExpression? ValidationFor { get; set; }
    // MVC injects this property before Process; the initial null is never a runtime fallback.
    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = null!;

    // Run after the MVC helpers so their generated attributes and errors are available.
    public override int Order => 1000;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var controller = ViewContext.RouteData.Values["controller"]?.ToString();
        if (controller?.StartsWith("Admin", StringComparison.Ordinal) != true) return;
        var expression = ValidationFor ?? For;
        if (expression is null) return;
        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name);
        var errorId = TagBuilder.CreateSanitizedId(name, "_") + "__error";
        if (ValidationFor is not null)
        {
            output.Attributes.SetAttribute("id", errorId);
            output.Attributes.SetAttribute("aria-live", "polite");
            return;
        }
        if (output.Attributes["type"]?.Value?.ToString() == "hidden") return;
        var existing = output.Attributes["aria-describedby"]?.Value?.ToString();
        output.Attributes.SetAttribute("aria-describedby", string.IsNullOrEmpty(existing) ? errorId : string.Join(" ", existing.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(errorId).Distinct(StringComparer.Ordinal)));
        if (ViewContext.ViewData.ModelState.TryGetValue(name, out var state) && state.Errors.Count > 0)
            output.Attributes.SetAttribute("aria-invalid", "true");
    }
}
