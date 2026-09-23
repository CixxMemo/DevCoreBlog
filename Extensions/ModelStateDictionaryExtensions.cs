using DevCoreBlog.Core.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevCoreBlog.Extensions;

public static class ModelStateDictionaryExtensions
{
    public static void AddContentErrors(
        this ModelStateDictionary modelState,
        ContentValidationResult result)
    {
        foreach (var error in result.Errors)
        {
            modelState.AddModelError(error.Field, error.Message);
        }
    }
}
