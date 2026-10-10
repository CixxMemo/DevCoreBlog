using System.Text.Json;
using DevCoreBlog.Core.Documents;
using static DevCoreBlog.Services.Documents.DocumentJsonFields;

namespace DevCoreBlog.Services.Documents;

/// <summary>Maps the v1 attribute allowlist into typed values, without HTML or CSS.</summary>
internal static class DocumentAttributeReader
{
    internal static DocumentAttributes? Read(DocumentNodeKind kind, JsonElement attrs, string path)
    {
        switch (kind)
        {
            case DocumentNodeKind.Paragraph:
                Fields(attrs, path, "textAlign");
                return new ParagraphAttributes(Alignment(attrs, path));
            case DocumentNodeKind.Heading:
                Fields(attrs, path, "level", "textAlign");
                var level = Integer(Required(attrs, "level", path), path + ".level");
                DocumentGuard.Require(level is >= 2 and <= 4, "heading.level", path);
                return new HeadingAttributes(level, Alignment(attrs, path));
            case DocumentNodeKind.OrderedList:
                Fields(attrs, path, "start", "type");
                DocumentGuard.Require(OptionalString(attrs, "type", path) is null or "1", "list.type", path);
                var start = OptionalInteger(attrs, "start", 1, path);
                DocumentGuard.Require(start >= 1, "list.start", path);
                DocumentGuard.Limit(start <= DocumentLimits.OrderedListStart, "list.start", path);
                return new OrderedListAttributes(start);
            case DocumentNodeKind.CodeBlock:
                Fields(attrs, path, "language");
                var language = OptionalString(attrs, "language", path);
                DocumentGuard.Require(language is null or "plaintext" or "csharp" or "javascript" or "typescript"
                    or "json" or "python" or "bash" or "sql" or "html" or "css" or "markdown", "code.language", path);
                return new CodeBlockAttributes(language);
            case DocumentNodeKind.Image:
                Fields(attrs, path, "src", "alt", "title", "width", "height");
                DocumentGuard.Require(IsAbsentOrNull(attrs, "width") && IsAbsentOrNull(attrs, "height"), "image.resize", path);
                var src = DocumentUrlPolicy.Read(String(Required(attrs, "src", path), path + ".src"), path + ".src", true);
                return new ImageAttributes(src, Label(attrs, "alt", path), Label(attrs, "title", path));
            case DocumentNodeKind.YouTube:
                Fields(attrs, path, "src", "width", "height", "start");
                DocumentGuard.Require(OptionalInteger(attrs, "width", 640, path) == 640
                    && OptionalInteger(attrs, "height", 480, path) == 480 && OptionalInteger(attrs, "start", 0, path) == 0,
                    "youtube.options", path);
                var source = String(Required(attrs, "src", path), path + ".src");
                return new YouTubeAttributes(source, DocumentUrlPolicy.YouTubeId(source, path + ".src"));
            case DocumentNodeKind.TableCell:
            case DocumentNodeKind.TableHeader:
                Fields(attrs, path, "colspan", "rowspan", "colwidth", "align");
                DocumentGuard.Require(OptionalInteger(attrs, "colspan", 1, path) == 1
                    && OptionalInteger(attrs, "rowspan", 1, path) == 1
                    && (!attrs.TryGetProperty("colwidth", out var width) || width.ValueKind == JsonValueKind.Null), "table.mergeOrResize", path);
                return new TableCellAttributes(Alignment(attrs, path, "align"));
            default:
                Fields(attrs, path);
                return null;
        }
    }

    internal static LinkAttributes Link(JsonElement attrs, string path)
    {
        Fields(attrs, path, "href", "title", "target", "rel", "class");
        var href = DocumentUrlPolicy.Read(String(Required(attrs, "href", path), path + ".href"), path + ".href", false);
        var target = OptionalString(attrs, "target", path);
        var rel = OptionalString(attrs, "rel", path);
        DocumentGuard.Require(target is null or "_self" or "_blank", "link.target", path);
        DocumentGuard.Require(rel is null or "noopener noreferrer" or "noopener noreferrer nofollow", "link.rel", path);
        DocumentGuard.Require(OptionalString(attrs, "class", path) is null, "link.class", path);
        return new LinkAttributes(href, Label(attrs, "title", path), target == "_blank" ? DocumentLinkTarget.NewWindow : DocumentLinkTarget.SameWindow,
            rel == "noopener noreferrer nofollow");
    }

    private static bool IsAbsentOrNull(JsonElement attrs, string name) => !attrs.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null;

    private static DocumentAlignment Alignment(JsonElement attrs, string path, string name = "textAlign") => OptionalString(attrs, name, path) switch
    {
        null or "left" => DocumentAlignment.Left,
        "center" => DocumentAlignment.Center,
        "right" => DocumentAlignment.Right,
        _ => throw new InvalidDocumentException(new(DocumentFailureKind.Schema, "attribute.alignment", path))
    };
}
