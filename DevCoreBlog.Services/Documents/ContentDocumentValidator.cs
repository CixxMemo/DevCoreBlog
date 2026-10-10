using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Core.Documents;
using static DevCoreBlog.Services.Documents.DocumentJsonFields;

namespace DevCoreBlog.Services.Documents;

/// <summary>One stateless boundary for all future save, preview, webhook and recovery inputs.</summary>
public sealed class ContentDocumentValidator
{
    public DocumentValidationResult Validate(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            using var json = DocumentJsonInput.Parse(utf8Json);
            Fields(json.RootElement, "$", "version", "document");
            var version = Integer(Required(json.RootElement, "version", "$"), "$.version");
            DocumentGuard.Require(version == DocumentLimits.Version, "schema.version", "$.version");
            var root = new DocumentTreeReader().Read(Required(json.RootElement, "document", "$"), "$.document", 1, false);
            DocumentGuard.Require(root.Kind == DocumentNodeKind.Doc, "schema.root", "$.document");
            return new(new ValidatedContentDocument(new ContentDocument(version, root)), null);
        }
        catch (InvalidDocumentException error) { return new(null, error.Error); }
        catch (DecoderFallbackException) { return Malformed("json.unicode"); }
        catch (JsonException) { return Malformed("json.syntax"); }
    }

    /// <summary>Checks the byte budget before encoding a caller's UTF-16 string.</summary>
    public DocumentValidationResult Validate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            if (json.Length > DocumentLimits.Utf8Bytes || DocumentJsonInput.StrictUtf8.GetByteCount(json) > DocumentLimits.Utf8Bytes)
                return new(null, new(DocumentFailureKind.Limit, "json.bytes", "$"));
            return Validate(DocumentJsonInput.StrictUtf8.GetBytes(json));
        }
        catch (EncoderFallbackException) { return Malformed("json.unicode"); }
    }

    private static DocumentValidationResult Malformed(string code) => new(null, new(DocumentFailureKind.Schema, code, "$"));
}

/// <summary>Request-local traversal state bounds resource use and preserves structural invariants.</summary>
internal sealed class DocumentTreeReader
{
    private static readonly JsonElement EmptyAttributes = JsonSerializer.SerializeToElement(new { });
    private int nodes, marks, textRunes, cells, images, videos;

    internal DocumentNode Read(JsonElement value, string path, int depth, bool insideCell)
    {
        DocumentGuard.Limit(depth <= DocumentLimits.NodeDepth, "document.depth", path);
        DocumentGuard.Limit(++nodes <= DocumentLimits.Nodes, "document.nodes", path);
        Fields(value, path, "type", "attrs", "text", "marks", "content");
        var kind = Kind(String(Required(value, "type", path), path + ".type"), path);
        DocumentGuard.Require(depth == 1 || kind != DocumentNodeKind.Doc, "schema.nestedDoc", path);
        DocumentGuard.Require(!insideCell || kind != DocumentNodeKind.Table, "table.nested", path);
        if (kind is DocumentNodeKind.TableCell or DocumentNodeKind.TableHeader)
            DocumentGuard.Limit(++cells <= DocumentLimits.TableCells, "document.cells", path);
        if (kind == DocumentNodeKind.Image) DocumentGuard.Limit(++images <= DocumentLimits.Images, "document.images", path);
        if (kind == DocumentNodeKind.YouTube) DocumentGuard.Limit(++videos <= DocumentLimits.Videos, "document.videos", path);

        var attrs = value.TryGetProperty("attrs", out var supplied) ? supplied : EmptyAttributes;
        var attributes = DocumentAttributeReader.Read(kind, attrs, path + ".attrs");
        if (kind == DocumentNodeKind.Text) return Text(value, path, attributes);
        DocumentGuard.Require(!value.TryGetProperty("text", out _) && !value.TryGetProperty("marks", out _), "schema.blockFields", path);
        var children = Children(value, kind, path, depth, insideCell || kind is DocumentNodeKind.TableCell or DocumentNodeKind.TableHeader);
        return new(kind, attributes, null, [], children);
    }

    private DocumentNode Text(JsonElement value, string path, DocumentAttributes? attributes)
    {
        DocumentGuard.Require(!value.TryGetProperty("content", out _), "schema.textContent", path);
        var text = String(Required(value, "text", path), path + ".text");
        DocumentGuard.Require(text.Length > 0 && !text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'), "text.characters", path);
        var length = Runes(text);
        DocumentGuard.Limit(length <= DocumentLimits.TextNodeRunes, "text.node", path);
        textRunes += length;
        DocumentGuard.Limit(textRunes <= DocumentLimits.TextRunes, "text.total", path);
        return new(DocumentNodeKind.Text, attributes, text, ReadMarks(value, path), []);
    }

    private ImmutableArray<DocumentMark> ReadMarks(JsonElement value, string path)
    {
        if (!value.TryGetProperty("marks", out var array)) return [];
        DocumentGuard.Require(array.ValueKind == JsonValueKind.Array, "schema.marks", path);
        DocumentGuard.Limit(array.GetArrayLength() <= DocumentLimits.MarksPerText, "marks.node", path);
        marks += array.GetArrayLength();
        DocumentGuard.Limit(marks <= DocumentLimits.Marks, "marks.total", path);
        var output = ImmutableArray.CreateBuilder<DocumentMark>();
        var seen = new HashSet<DocumentMarkKind>();
        var index = 0;
        foreach (var mark in array.EnumerateArray())
        {
            var markPath = path + ".marks[" + index++ + "]";
            Fields(mark, markPath, "type", "attrs");
            var type = String(Required(mark, "type", markPath), markPath + ".type");
            var kind = type switch
            {
                "bold" => DocumentMarkKind.Bold, "italic" => DocumentMarkKind.Italic,
                "underline" => DocumentMarkKind.Underline, "code" => DocumentMarkKind.Code, "link" => DocumentMarkKind.Link,
                _ => throw new InvalidDocumentException(new(DocumentFailureKind.Schema, "schema.mark", markPath))
            };
            DocumentGuard.Require(seen.Add(kind), "marks.duplicate", markPath);
            var attrs = mark.TryGetProperty("attrs", out var supplied) ? supplied : EmptyAttributes;
            LinkAttributes? link = null;
            if (kind == DocumentMarkKind.Link) link = DocumentAttributeReader.Link(attrs, markPath + ".attrs");
            else Fields(attrs, markPath + ".attrs");
            output.Add(new(kind, link));
        }
        return output.ToImmutable();
    }

    private ImmutableArray<DocumentNode> Children(JsonElement value, DocumentNodeKind kind, string path, int depth, bool insideCell)
    {
        var leaf = kind is DocumentNodeKind.Image or DocumentNodeKind.YouTube or DocumentNodeKind.HardBreak;
        var hasContent = value.TryGetProperty("content", out var array);
        DocumentGuard.Require(!leaf || !hasContent, "schema.leafContent", path);
        if (leaf) return [];
        DocumentGuard.Require(!hasContent || array.ValueKind == JsonValueKind.Array, "schema.content", path);
        var count = hasContent ? array.GetArrayLength() : 0;
        var canBeEmpty = kind is DocumentNodeKind.Paragraph or DocumentNodeKind.Heading or DocumentNodeKind.CodeBlock;
        DocumentGuard.Require(canBeEmpty || count > 0, "schema.emptyBlock", path);
        if (kind == DocumentNodeKind.Table) DocumentGuard.Limit(count <= DocumentLimits.TableRows, "table.rows", path);
        if (kind == DocumentNodeKind.TableRow) DocumentGuard.Limit(count <= DocumentLimits.TableColumns, "table.columns", path);
        var output = ImmutableArray.CreateBuilder<DocumentNode>();
        var index = 0;
        if (hasContent)
        {
            foreach (var child in array.EnumerateArray())
            {
                var childPath = path + ".content[" + index + "]";
                var parsed = Read(child, childPath, depth + 1, insideCell);
                DocumentGuard.Require(AllowedChild(kind, parsed.Kind, index), "schema.child", childPath);
                DocumentGuard.Require(kind != DocumentNodeKind.CodeBlock || parsed.Marks.IsEmpty, "code.marks", childPath);
                if (kind == DocumentNodeKind.Table && output.Count > 0)
                    DocumentGuard.Require(parsed.Children.Length == output[0].Children.Length, "table.rectangle", childPath);
                output.Add(parsed);
                index++;
            }
        }
        return output.ToImmutable();
    }

    private static bool AllowedChild(DocumentNodeKind parent, DocumentNodeKind child, int index) => parent switch
    {
        DocumentNodeKind.Paragraph or DocumentNodeKind.Heading => child is DocumentNodeKind.Text or DocumentNodeKind.HardBreak,
        DocumentNodeKind.CodeBlock => child == DocumentNodeKind.Text,
        DocumentNodeKind.BulletList or DocumentNodeKind.OrderedList => child == DocumentNodeKind.ListItem,
        DocumentNodeKind.ListItem when index == 0 => child == DocumentNodeKind.Paragraph,
        DocumentNodeKind.Table => child == DocumentNodeKind.TableRow,
        DocumentNodeKind.TableRow => child is DocumentNodeKind.TableCell or DocumentNodeKind.TableHeader,
        _ => child is DocumentNodeKind.Paragraph or DocumentNodeKind.Heading or DocumentNodeKind.Blockquote
            or DocumentNodeKind.BulletList or DocumentNodeKind.OrderedList or DocumentNodeKind.CodeBlock
            or DocumentNodeKind.Image or DocumentNodeKind.YouTube or DocumentNodeKind.Table
    };

    private static DocumentNodeKind Kind(string type, string path) => type switch
    {
        "doc" => DocumentNodeKind.Doc, "paragraph" => DocumentNodeKind.Paragraph, "heading" => DocumentNodeKind.Heading,
        "text" => DocumentNodeKind.Text, "hardBreak" => DocumentNodeKind.HardBreak, "blockquote" => DocumentNodeKind.Blockquote,
        "bulletList" => DocumentNodeKind.BulletList, "orderedList" => DocumentNodeKind.OrderedList, "listItem" => DocumentNodeKind.ListItem,
        "codeBlock" => DocumentNodeKind.CodeBlock, "image" => DocumentNodeKind.Image, "youtube" => DocumentNodeKind.YouTube,
        "table" => DocumentNodeKind.Table, "tableRow" => DocumentNodeKind.TableRow, "tableCell" => DocumentNodeKind.TableCell,
        "tableHeader" => DocumentNodeKind.TableHeader,
        _ => throw new InvalidDocumentException(new(DocumentFailureKind.Schema, "schema.node", path))
    };
}
