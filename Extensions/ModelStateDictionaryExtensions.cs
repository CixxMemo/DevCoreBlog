using DevCoreBlog.Core.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DevCoreBlog.Extensions;

/// <summary>Preserves domain errors and safe form redisplay without changing validation outcomes.</summary>
public static class ModelStateDictionaryExtensions
{
    /// <summary>Malformed checkbox text must retain its error instead of throwing during Razor conversion.</summary>
    public static void PrepareBooleanRedisplay(this ModelStateDictionary modelState, string field, bool fallback)
    {
        if (modelState.TryGetValue(field, out var entry) && entry.Errors.Count > 0)
            modelState.SetModelValue(field, fallback, fallback ? "true" : "false");
    }

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
