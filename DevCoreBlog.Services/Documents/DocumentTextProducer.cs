using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Produces deterministic reading facts without reparsing JSON or generating HTML.</summary>
public sealed class DocumentTextProducer
{
    public const int WordsPerMinute = 200;

    public DocumentReading Produce(ValidatedContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var text = new BlockText();
        var headings = ImmutableArray.CreateBuilder<DocumentHeading>();
        Walk(document.Document.Root, text, headings);
        var plainText = text.ToString();
        var words = CountWords(plainText);
        return new(plainText, words, (words + WordsPerMinute - 1) / WordsPerMinute, headings.ToImmutable());
    }

    private static void Walk(DocumentNode node, BlockText text, ImmutableArray<DocumentHeading>.Builder headings)
    {
        if (node.Kind == DocumentNodeKind.Text)
        {
            text.Append(node.Text ?? throw new InvalidOperationException("Validated text is missing."));
            return;
        }
        if (node.Kind == DocumentNodeKind.HardBreak)
        {
            text.Append("\n");
            return;
        }
        text.Boundary();
        if (node.Kind == DocumentNodeKind.Heading)
        {
            var attributes = node.Attributes as HeadingAttributes
                ?? throw new InvalidOperationException("Validated heading attributes are missing.");
            var title = new StringBuilder();
            foreach (var child in node.Children)
                title.Append(child.Kind == DocumentNodeKind.HardBreak ? "\n" : child.Text);
            headings.Add(new("document-section-" + (headings.Count + 1).ToString(CultureInfo.InvariantCulture),
                title.ToString(), attributes.Level));
        }
        // Media has no children: URLs, alt/title and video IDs never become reading text.
        foreach (var child in node.Children) Walk(child, text, headings);
        text.Boundary();
    }

    private static int CountWords(string text)
    {
        var words = 0;
        var inWord = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetterOrDigit(rune) || rune.Value == '_')
            {
                if (!inWord) words++;
                inWord = true;
            }
            else if (category is not UnicodeCategory.NonSpacingMark and not UnicodeCategory.SpacingCombiningMark
                and not UnicodeCategory.EnclosingMark)
                inWord = false;
            // Combining marks continue an existing word; standalone marks never start one.
        }
        return words;
    }

    /// <summary>Separates blocks lazily without trimming authored text or splitting inline runs.</summary>
    private sealed class BlockText
    {
        private readonly StringBuilder value = new();
        private bool pendingBoundary;

        internal void Boundary() => pendingBoundary = true;

        internal void Append(string text)
        {
            if (pendingBoundary && value.Length > 0 && value[^1] != '\n') value.Append('\n');
            pendingBoundary = false;
            value.Append(text);
        }

        public override string ToString() => value.ToString();
    }
}
