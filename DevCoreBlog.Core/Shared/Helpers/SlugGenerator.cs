// =============================================================================
// SlugGenerator.cs — URL-Friendly Slug Generation Helper
// =============================================================================
// This static helper class converts arbitrary text (e.g. blog post title or
// category name) into a URL-friendly "slug" string.
//
// Example: "ASP.NET Core & Modern C# 13 Guide!" -> "aspnet-core-modern-c-13-guide"
// Example: "C# Dersleri — Giriş" -> "c-dersleri-giris"
//
// Features:
//   - Replaces specific Turkish and special characters (ç, ğ, ı, ö, ş, ü)
//   - Normalizes Unicode accents and diacritics (e.g., é -> e, à -> a)
//   - Converts to lowercase ASCII
//   - Replaces spaces and punctuation with hyphens
//   - Collapses consecutive hyphens and trims leading/trailing hyphens
// =============================================================================

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DevCoreBlog.Core.Shared.Helpers;

// Static class — cannot be instantiated, accessed as SlugGenerator.Generate(...)
public static class SlugGenerator
{
    // Converts the given text into a clean, lowercase URL-friendly slug string
    public static string Generate(string text)
    {
        // Guard clause: return empty string if input is null, empty, or whitespace
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Step 1: Map specific Turkish characters to ASCII before standard normalization
        var normalized = text.Trim()
            .Replace("ı", "i").Replace("İ", "i")
            .Replace("ğ", "g").Replace("Ğ", "g")
            .Replace("ü", "u").Replace("Ü", "u")
            .Replace("ş", "s").Replace("Ş", "s")
            .Replace("ö", "o").Replace("Ö", "o")
            .Replace("ç", "c").Replace("Ç", "c");

        // Step 2: Normalize Unicode to FormD to decompose accented characters (e.g., 'é' -> 'e' + diacritic)
        var formD = normalized.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        // Step 3: Remove non-spacing marks (diacritic accents)
        foreach (var ch in formD)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        // Step 4: Convert to standard FormC and lowercase invariant
        var cleanText = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();

        // Step 5: Remove all characters that are NOT lowercase ASCII letters, digits, spaces, or hyphens
        cleanText = Regex.Replace(cleanText, @"[^a-z0-9\s-]", "");

        // Step 6: Replace one or more spaces/hyphens with a single hyphen, then trim hyphens from edges
        cleanText = Regex.Replace(cleanText, @"[\s-]+", "-").Trim('-');

        // Return the final URL-friendly slug (e.g. "building-modern-apps-with-net-10")
        return cleanText;
    }
}
