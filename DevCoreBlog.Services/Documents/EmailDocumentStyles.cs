using System.Collections.Immutable;
using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Closed email styles shared with the preview CSP hash boundary; authored CSS is never accepted.</summary>
public static class EmailDocumentStyles
{
    public const string Body = "font-family:Arial,sans-serif;line-height:1.6;color:#111111;overflow-wrap:anywhere;";
    public const string Code = "white-space:pre-wrap;overflow-wrap:anywhere;font-family:monospace;";
    public const string Table = "width:100%;table-layout:fixed;border-collapse:collapse;";
    public const string Image = "display:block;max-width:100%;height:auto;border:0;";

    public static ImmutableArray<string> All { get; } = [Body, Code, Table, Image,
        Alignment(DocumentAlignment.Left, false), Alignment(DocumentAlignment.Center, false), Alignment(DocumentAlignment.Right, false),
        Alignment(DocumentAlignment.Left, true), Alignment(DocumentAlignment.Center, true), Alignment(DocumentAlignment.Right, true)];

    public static string Alignment(DocumentAlignment alignment, bool cell)
    {
        var value = alignment switch
        {
            DocumentAlignment.Left => "left", DocumentAlignment.Center => "center", DocumentAlignment.Right => "right",
            _ => throw new InvalidOperationException("Unsupported validated alignment.")
        };
        return "text-align:" + value + (cell ? ";border:1px solid #cccccc;padding:8px;vertical-align:top;overflow-wrap:anywhere" : "") + ";";
    }
}
