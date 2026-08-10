// =============================================================================
// SlugGenerator.cs — URL-Friendly Slug Generation Helper
// =============================================================================
// This static helper class converts arbitrary text (like a blog post title or
// category name) into a URL-friendly "slug" string.
//
// Example: "C# Dersleri — Giriş" → "c-dersleri-giris"
//
// Features:
//   - Turkish character conversion (ç→c, ğ→g, ı→i, ö→o, ş→s, ü→u)
//   - Converts to lowercase
//   - Removes special characters (keeps only letters, digits, spaces, hyphens)
//   - Replaces spaces and multiple hyphens with a single hyphen
//   - Trims leading/trailing hyphens
// =============================================================================

// Import Regex for pattern-based string replacement
using System.Text.RegularExpressions;

namespace DevCoreBlog.Shared.Helpers;

// Static class — cannot be instantiated, accessed as SlugGenerator.Generate(...)
public static class SlugGenerator
{
    // Converts the given text into a URL-friendly slug string
    public static string Generate(string text)
    {
        // Return empty string if input is null, empty, or whitespace-only
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Turkish-to-English character mapping (lowercase keys since we convert to lowercase first)
        var trMap = new Dictionary<char, string>
        {
            { 'ç', "c" }, { 'ğ', "g" }, { 'ı', "i" }, { 'ö', "o" }, { 'ş', "s" }, { 'ü', "u" },
            { 'Ç', "C" }, { 'Ğ', "G" }, { 'İ', "I" }, { 'Ö', "O" }, { 'Ş', "S" }, { 'Ü', "U" }
        };

        // Step 1: Convert the entire text to lowercase
        var result = text.ToLower();

        // Step 2: Replace each Turkish character with its English equivalent
        foreach (var kvp in trMap)
        {
            result = result.Replace(kvp.Key.ToString(), kvp.Value.ToLower());
        }

        // Step 3: Remove all characters that are NOT letters, digits, spaces, or hyphens
        // Regex [^a-z0-9\s-] matches anything outside the allowed set
        result = Regex.Replace(result, @"[^a-z0-9\s-]", "");

        // Step 4: Replace one or more spaces/hyphens with a single hyphen, then trim edges
        // [\s-]+ matches consecutive whitespace or hyphens
        result = Regex.Replace(result, @"[\s-]+", "-").Trim('-');

        // Return the final slug (e.g. "asp-net-core-ile-blog-yazma")
        return result;
    }
}
