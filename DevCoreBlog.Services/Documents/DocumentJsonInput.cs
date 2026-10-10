using System.Text;
using System.Text.Json;
using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Rejects ambiguous keys and invalid Unicode before allocating a JSON tree.</summary>
internal static class DocumentJsonInput
{
    // Node objects alternate with child arrays; the extra levels cover envelope and attributes.
    internal const int SyntaxDepth = DocumentLimits.NodeDepth * 2 + 8;
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static JsonDocument Parse(ReadOnlyMemory<byte> input)
    {
        DocumentGuard.Limit(input.Length <= DocumentLimits.Utf8Bytes, "json.bytes", "$");
        StrictUtf8.GetCharCount(input.Span);
        var reader = new Utf8JsonReader(input.Span, new JsonReaderOptions { MaxDepth = SyntaxDepth });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
            {
                string? value;
                try { value = reader.GetString(); }
                catch (InvalidOperationException)
                {
                    // Utf8JsonReader rejects escaped unpaired surrogates when decoding a string.
                    throw new InvalidDocumentException(new(DocumentFailureKind.Schema, "json.unicode", "$"));
                }
                DocumentGuard.Require(value is not null && IsUnicode(value), "json.unicode", "$");
                if (reader.TokenType == JsonTokenType.PropertyName && value is not null)
                    DocumentGuard.Require(objects.Peek().Add(value), "json.duplicate", "$");
            }
        }
        return JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = SyntaxDepth });
    }

    private static bool IsUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index])) continue;
            if (!char.IsHighSurrogate(value[index]) || index + 1 >= value.Length || !char.IsLowSurrogate(value[++index]))
                return false;
        }
        return true;
    }
}

/// <summary>Small strict field readers keep schema checks separate from tree traversal.</summary>
internal static class DocumentJsonFields
{
    internal static void Fields(JsonElement value, string path, params string[] allowed)
    {
        DocumentGuard.Require(value.ValueKind == JsonValueKind.Object, "schema.object", path);
        foreach (var property in value.EnumerateObject())
            DocumentGuard.Require(Array.IndexOf(allowed, property.Name) >= 0, "schema.field", path);
    }

    internal static JsonElement Required(JsonElement value, string name, string path)
    {
        DocumentGuard.Require(value.TryGetProperty(name, out var field), "schema.required", path + "." + name);
        return field;
    }

    internal static string String(JsonElement value, string path)
    {
        DocumentGuard.Require(value.ValueKind == JsonValueKind.String, "schema.string", path);
        return value.GetString() ?? throw new InvalidDocumentException(new(DocumentFailureKind.Schema, "schema.string", path));
    }

    internal static string? OptionalString(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        return String(field, path + "." + name);
    }

    internal static int Integer(JsonElement value, string path)
    {
        DocumentGuard.Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _), "schema.integer", path);
        return value.GetInt32();
    }

    internal static int OptionalInteger(JsonElement value, string name, int fallback, string path) =>
        value.TryGetProperty(name, out var field) ? Integer(field, path + "." + name) : fallback;

    internal static int Runes(string value) => value.EnumerateRunes().Count();

    internal static string? Label(JsonElement value, string name, string path)
    {
        var label = OptionalString(value, name, path);
        DocumentGuard.Limit(label is null || Runes(label) <= DocumentLimits.LabelRunes, "attribute.label", path + "." + name);
        DocumentGuard.Require(label is null || !label.Any(c => char.IsControl(c)), "attribute.control", path + "." + name);
        return label;
    }
}
