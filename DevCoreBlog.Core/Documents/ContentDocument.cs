using System.Collections.Immutable;

namespace DevCoreBlog.Core.Documents;

/// <summary>Immutable, provider-independent values for the supported document schema.</summary>
public sealed record ContentDocument(int Version, DocumentNode Root);

/// <summary>Typed tree data; only the validation boundary grants a trusted document.</summary>
public sealed record DocumentNode(
    DocumentNodeKind Kind,
    DocumentAttributes? Attributes,
    string? Text,
    ImmutableArray<DocumentMark> Marks,
    ImmutableArray<DocumentNode> Children);

public enum DocumentNodeKind
{
    Doc, Paragraph, Heading, Text, HardBreak, Blockquote, BulletList, OrderedList,
    ListItem, CodeBlock, Image, YouTube, Table, TableRow, TableCell, TableHeader
}

public enum DocumentMarkKind { Bold, Italic, Underline, Code, Link }
public enum DocumentAlignment { Left, Center, Right }
public enum DocumentLinkTarget { SameWindow, NewWindow }

/// <summary>Closed attributes replace untrusted dictionaries and JSON elements.</summary>
public abstract record DocumentAttributes;
public sealed record ParagraphAttributes(DocumentAlignment Alignment) : DocumentAttributes;
public sealed record HeadingAttributes(int Level, DocumentAlignment Alignment) : DocumentAttributes;
public sealed record OrderedListAttributes(int Start) : DocumentAttributes;
public sealed record CodeBlockAttributes(string? Language) : DocumentAttributes;
public sealed record TableCellAttributes(DocumentAlignment Alignment) : DocumentAttributes;
public sealed record ImageAttributes(string Source, string? Alt, string? Title) : DocumentAttributes;
public sealed record YouTubeAttributes(string Source, string VideoId) : DocumentAttributes;
public sealed record LinkAttributes(string Href, string? Title, DocumentLinkTarget Target, bool NoFollow) : DocumentAttributes;
public sealed record DocumentMark(DocumentMarkKind Kind, LinkAttributes? Link);

/// <summary>Hard resource limits shared by validation and future document consumers.</summary>
public static class DocumentLimits
{
    public const int Version = 1;
    public const int Utf8Bytes = 1_048_576;
    public const int NodeDepth = 32;
    public const int Nodes = 10_000;
    public const int Marks = 30_000;
    public const int MarksPerText = 5;
    public const int TextRunes = 200_000;
    public const int TextNodeRunes = 20_000;
    public const int UrlRunes = 2_048;
    public const int LabelRunes = 300;
    public const int TableRows = 20;
    public const int TableColumns = 10;
    public const int TableCells = 1_000;
    public const int Images = 50;
    public const int Videos = 10;
    public const int OrderedListStart = 10_000;
}
