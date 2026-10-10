using System.Globalization;
using System.Text.Json;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Rendering;

/// <summary>Behavioral fixtures for visible text, Unicode, reading thresholds and deterministic targets.</summary>
internal static class DocumentReadingChecks
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        var validator = new ContentDocumentValidator();
        var producer = new DocumentTextProducer();
        DocumentReading Read(string blocks)
        {
            var result = validator.Validate("{\"version\":1,\"document\":" + Node("doc", blocks) + "}");
            return producer.Produce(result.Document ?? throw new InvalidOperationException("Synthetic fixture is invalid."));
        }
        void Expect(string name, string blocks, string text, int words, int minutes) => check("reading." + name, () =>
        {
            var result = Read(blocks);
            return result.PlainText == text && result.WordCount == words && result.ReadingMinutes == minutes;
        });

        Expect("empty", P(), "", 0, 0);
        Expect("spaces", P(" \t "), " \t ", 0, 0);
        Expect("punctuation", P("...!?—"), "...!?—", 0, 0);
        Expect("mediaOnly", Image() + "," + Video(), "", 0, 0);
        Expect("turkish", P("ı İ ç Ç ş Ş ğ Ğ ö Ö ü Ü"), "ı İ ç Ç ş Ş ğ Ğ ö Ö ü Ü", 12, 1);
        Expect("emoji", P("😀👨‍👩‍👧‍👦👍🏽"), "😀👨‍👩‍👧‍👦👍🏽", 0, 0);
        Expect("unicodeCombining", P("cafe\u0301nin कि"), "cafe\u0301nin कि", 2, 1);
        Expect("standaloneCombining", P("\u0301 \u0308"), "\u0301 \u0308", 0, 0);
        Expect("astralLetter", P("𐐀𐐁"), "𐐀𐐁", 1, 1);
        Expect("tokenDefinition", P("Türkçe'nin foo-bar C# .NET 123 _x A\u0301B"), "Türkçe'nin foo-bar C# .NET 123 _x A\u0301B", 9, 1);
        Expect("paragraphs", P("bir") + "," + P("iki"), "bir\niki", 2, 1);
        Expect("emptyBlocksCollapse", P("bir") + "," + P() + "," + P("iki"), "bir\niki", 2, 1);
        Expect("inlineRunsJoin", Node("paragraph", T("Tür") + "," + T("kçe", "{\"type\":\"bold\"}")), "Türkçe", 1, 1);
        Expect("combiningAcrossRuns", Node("paragraph", T("cafe") + "," + T("\u0301nin", "{\"type\":\"italic\"}")), "cafe\u0301nin", 1, 1);
        Expect("hardBreak", Node("paragraph", T("bir") + "," + Node("hardBreak") + "," + T("iki")), "bir\niki", 2, 1);
        Expect("authoredBlankLines", Node("paragraph", T("bir") + "," + Node("hardBreak") + "," + Node("hardBreak") + "," + T("iki")), "bir\n\niki", 2, 1);
        Expect("trailingHardBreak", Node("paragraph", T("bir") + "," + Node("hardBreak")), "bir\n", 1, 1);
        Expect("linkLabelOnly", Node("paragraph", T("etiket", "{\"type\":\"link\",\"attrs\":{\"href\":\"https://example.test/URL_SENTINEL\",\"title\":\"TITLE_SENTINEL\"}}")), "etiket", 1, 1);
        Expect("inlineCode", Node("paragraph", T("kod", "{\"type\":\"code\"}")), "kod", 1, 1);
        Expect("codeWhitespace", Node("codeBlock", T("  var") + "," + T(" x=1;\r\n"), "{\"language\":\"csharp\"}"), "  var x=1;\r\n", 3, 1);
        Expect("codeBlockBoundary", P("önce") + "," + Node("codeBlock", T("sonra")), "önce\nsonra", 2, 1);
        Expect("blockquote", P("önce") + "," + Node("blockquote", P("alıntı")) + "," + P("sonra"), "önce\nalıntı\nsonra", 3, 1);
        Expect("nestedLists", Node("bulletList", Node("listItem", P("bir") + "," + Node("orderedList", Node("listItem", P("iki")), "{\"start\":10000}")) + "," + Node("listItem", P("üç"))), "bir\niki\nüç", 3, 1);
        var table = Node("table", Node("tableRow", Node("tableHeader", P("başlık")) + "," + Node("tableCell", P("ikinci")))
            + "," + Node("tableRow", Node("tableCell", P("üçüncü") + "," + P("alt")) + "," + Node("tableCell", P("dört"))));
        Expect("tableBoundaries", table, "başlık\nikinci\nüçüncü\nalt\ndört", 5, 1);
        Expect("mediaMetadataExcluded", P("bir") + "," + Image() + "," + Video() + "," + P("iki"), "bir\niki", 2, 1);
        Expect("literalTagsAreText", P("<script>alert(1)</script>"), "<script>alert(1)</script>", 4, 1);
        foreach (var count in new[] { 1, 199, 200, 201, 399, 400, 401 })
            Expect("threshold." + count, P(string.Join(' ', Enumerable.Repeat("kelime", count))),
                string.Join(' ', Enumerable.Repeat("kelime", count)), count, (count + 199) / 200);

        var headings = H("Aynı 😀", 2) + "," + Node("blockquote", H("Aynı 😀", 3)) + "," + H("", 4)
            + "," + Node("bulletList", Node("listItem", P("madde") + "," + H("İğne", 2)));
        check("reading.headingOrderAndDuplicates", () =>
        {
            var r = Read(headings);
            return r.Headings.Select(h => h.Id).SequenceEqual(new[] { "document-section-1", "document-section-2", "document-section-3", "document-section-4" })
                && r.Headings.Select(h => h.Title).SequenceEqual(new[] { "Aynı 😀", "Aynı 😀", "", "İğne" })
                && r.Headings.Select(h => h.Level).SequenceEqual(new[] { 2, 3, 4, 2 });
        });
        check("reading.headingInlineAndBreak", () =>
        {
            var r = Read(Node("heading", T("Tür", "{\"type\":\"bold\"}") + "," + T("kçe") + "," + Node("hardBreak") + "," + T("😀"), "{\"level\":2}"));
            return r.Headings[0].Title == "Türkçe\n😀" && r.PlainText == "Türkçe\n😀" && r.WordCount == 1;
        });
        check("reading.emptyHeadingHasZeroMinutes", () => { var r = Read(H("", 2)); return r.WordCount == 0 && r.ReadingMinutes == 0 && r.Headings.Length == 1; });
        check("reading.safeIdsDespiteLiteralTitle", () => Read(H("\" onclick='x' <b>😀</b>", 2)).Headings[0].Id == "document-section-1");
        check("reading.cultureIndependent", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                var a = JsonSerializer.Serialize(Read(headings));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                var b = JsonSerializer.Serialize(Read(headings));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
                return a == b && a == JsonSerializer.Serialize(Read(headings));
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
        check("reading.deterministicAndNoMutation", () =>
        {
            var result = validator.Validate("{\"version\":1,\"document\":" + Node("doc", headings) + "}");
            var document = result.Document ?? throw new InvalidOperationException();
            var before = JsonSerializer.Serialize(document.Document);
            var a = JsonSerializer.Serialize(producer.Produce(document));
            return a == JsonSerializer.Serialize(producer.Produce(document)) && before == JsonSerializer.Serialize(document.Document);
        });
        check("reading.parallelIsolation", () =>
        {
            var outcomes = new bool[64];
            Parallel.For(0, outcomes.Length, i => outcomes[i] = Read(i % 2 == 0 ? headings : P()).Headings.Length == (i % 2 == 0 ? 4 : 0));
            return outcomes.All(x => x);
        });
        check("reading.maxText", () =>
        {
            var blocks = string.Join(',', Enumerable.Repeat(P(string.Concat(Enumerable.Repeat("a ", 10000))), 10));
            var r = Read(blocks);
            return r.WordCount == 100000 && r.ReadingMinutes == 500 && r.PlainText.Length == 200009;
        });
        check("reading.maxNodes", () => { var r = Read(string.Join(',', Enumerable.Repeat(P(), 9999))); return r.PlainText == "" && r.ReadingMinutes == 0; });
        check("reading.maxHeadingCount", () =>
        {
            var r = Read(string.Join(',', Enumerable.Repeat(H("x", 2), 4999)));
            return r.Headings.Length == 4999 && r.Headings[^1].Id == "document-section-4999" && r.Headings.Select(h => h.Id).Distinct().Count() == 4999;
        });
        check("reading.maxDepth", () => { var n = P("derin"); for (var i = 0; i < 29; i++) n = Node("blockquote", n); return Read(n).PlainText == "derin"; });
        var legacy = new SafeMarkdownRenderer();
        foreach (var (name, markdown, blocks) in new[] {
            ("paragraph", "Türkçe **metin** ve [etiket](https://example.test/)", Node("paragraph", T("Türkçe ") + "," + T("metin", "{\"type\":\"bold\"}") + "," + T(" ve etiket"))),
            ("list", "- bir\n- iki", Node("bulletList", Node("listItem", P("bir")) + "," + Node("listItem", P("iki")))),
            ("code", "```csharp\nvar x = 1;\n```", Node("codeBlock", T("var x = 1;"), "{\"language\":\"csharp\"}")) })
            check("reading.legacyComparison." + name, () =>
            {
                var r = Read(blocks);
                var normalized = string.Join(' ', r.PlainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                return normalized == legacy.ToPlainText(markdown) && r.ReadingMinutes == legacy.RenderDocument(markdown).ReadingMinutes;
            });
        check("reading.legacyEmptyDifferenceExplicit", () => Read(P()).ReadingMinutes == 0 && legacy.RenderDocument("").ReadingMinutes == 1);
    }

    private static string Q(string value) => JsonSerializer.Serialize(value);
    private static string Node(string kind, string? children = null, string? attrs = null) => "{\"type\":" + Q(kind)
        + (attrs is null ? "" : ",\"attrs\":" + attrs) + (children is null ? "" : ",\"content\":[" + children + "]") + "}";
    private static string T(string text, string? marks = null) => "{\"type\":\"text\",\"text\":" + Q(text)
        + (marks is null ? "" : ",\"marks\":[" + marks + "]") + "}";
    private static string P(string? text = null) => Node("paragraph", text is null ? null : T(text));
    private static string H(string text, int level) => Node("heading", text.Length == 0 ? null : T(text), "{\"level\":" + level + "}");
    private static string Image() => Node("image", attrs: "{\"src\":\"https://example.test/IMAGE_URL_SENTINEL.gif\",\"alt\":\"ALT_SENTINEL\",\"title\":\"TITLE_SENTINEL\"}");
    private static string Video() => Node("youtube", attrs: "{\"src\":\"https://youtu.be/abcdefghijk\"}");
}
