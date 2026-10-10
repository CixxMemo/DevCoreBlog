using System.Text;
using System.Text.Json;
using DevCoreBlog.Core.Documents;
using DevCoreBlog.Services.Documents;

// Pure synthetic acceptance: no host, environment configuration, database or URL requests.
var validator = new ContentDocumentValidator();
var checks = new SortedDictionary<string, bool>(StringComparer.Ordinal);
var failures = new Dictionary<string, string>();

void Check(string name, Func<bool> assertion)
{
    try { checks[name] = assertion(); }
    catch (Exception error) { checks[name] = false; failures[name] = error.GetType().Name; }
}

bool Valid(string input)
{
    var result = validator.Validate(input);
    return result.IsValid && result.Document is not null && result.Error is null;
}

bool Invalid(string input, string? code = null, DocumentFailureKind? kind = null)
{
    var result = validator.Validate(input);
    return !result.IsValid && result.Document is null && result.Error is not null
        && (code is null || result.Error.Code == code) && (kind is null || result.Error.Kind == kind);
}

void Boundary(string name, string at, string above, string code) =>
    Check("limit." + name, () => Valid(at) && Invalid(above, code, DocumentFailureKind.Limit));

string Quote(string value) => JsonSerializer.Serialize(value);
string Node(string type, string? children = null, string? attrs = null) =>
    "{\"type\":" + Quote(type) + (attrs is null ? "" : ",\"attrs\":" + attrs)
    + (children is null ? "" : ",\"content\":[" + children + "]") + "}";
string Text(string text, string? marks = null) => "{\"type\":\"text\",\"text\":" + Quote(text)
    + (marks is null ? "" : ",\"marks\":[" + marks + "]") + "}";
string Paragraph(string? text = null) => Node("paragraph", text is null ? null : Text(text));
string Doc(string nodes) => "{\"version\":1,\"document\":" + Node("doc", nodes) + "}";
string Repeat(string value, int count) => string.Join(',', Enumerable.Repeat(value, count));
string Mark(string type) => "{\"type\":" + Quote(type) + "}";
string Link(string href, string extra = "") => "{\"type\":\"link\",\"attrs\":{\"href\":" + Quote(href) + extra + "}}";
string Image(string alt = "", string src = "https://res.cloudinary.com/demo/image/upload/sample.gif", string extra = "") =>
    Node("image", attrs: "{\"src\":" + Quote(src) + ",\"alt\":" + Quote(alt) + extra + "}");
string Video(string src = "https://youtu.be/abcdefghijk", string extra = "") =>
    Node("youtube", attrs: "{\"src\":" + Quote(src) + extra + "}");
string Table(int rows, int columns, string? cell = null) => Node("table", Repeat(Node("tableRow", Repeat(cell ?? Node("tableCell", Paragraph()), columns)), rows));
string Nested(int quoteCount) { var value = Paragraph("x"); for (var i = 0; i < quoteCount; i++) value = Node("blockquote", value); return Doc(value); }
var allMarks = string.Join(',', new[] { Mark("bold"), Mark("italic"), Mark("underline"), Mark("code"), Link("/") });
var empty = Doc(Paragraph());

Check("positive.emptyParagraph", () => Valid(empty));
Check("positive.allNodes", () => Valid(Doc(string.Join(',', new[] {
    Node("paragraph", Text("Türkçe: ıİğĞşŞöÖçÇüÜ 😀") + "," + Node("hardBreak") + "," + Text("son", allMarks)),
    Node("heading", Text("Başlık"), "{\"level\":2,\"textAlign\":\"center\"}"),
    Node("blockquote", Paragraph("alıntı")),
    Node("bulletList", Node("listItem", Paragraph("madde") + "," + Node("orderedList", Node("listItem", Paragraph("alt")), "{\"start\":2}"))),
    Node("codeBlock", Text("<script>\nconst a = 1;\n</script>"), "{\"language\":\"javascript\"}"),
    Image("animasyon"), Video(), Table(2, 2, Node("tableHeader", Paragraph("hücre"), "{\"colspan\":1,\"rowspan\":1,\"colwidth\":null}"))
}))));
foreach (var level in new[] { 2, 3, 4 })
    Check("positive.heading" + level, () => Valid(Doc(Node("heading", Text("h"), "{\"level\":" + level + "}"))));
foreach (var align in new[] { "left", "center", "right" })
    Check("positive.align." + align, () => Valid(Doc(Node("paragraph", Text("p"), "{\"textAlign\":" + Quote(align) + "}"))));
foreach (var language in new[] { "plaintext", "csharp", "javascript", "typescript", "json", "python", "bash", "sql", "html", "css", "markdown" })
    Check("positive.code." + language, () => Valid(Doc(Node("codeBlock", Text("x"), "{\"language\":" + Quote(language) + "}"))));
foreach (var url in new[] { "https://example.test/a", "HTTPS://example.test/a?x=1&y=2", "/", "/yazilar/deney?q=x#h", "#başlık", "https://example.test/%C4%B1" })
    Check("positive.url." + checks.Count, () => Valid(Doc(Node("paragraph", Text("link", Link(url))))));
foreach (var url in new[] { "https://youtu.be/abcdefghijk", "https://www.youtube.com/watch?v=abcdefghijk", "https://youtube.com/embed/abcdefghijk", "https://www.youtube-nocookie.com/embed/abcdefghijk" })
    Check("positive.youtube." + checks.Count, () => Valid(Doc(Video(url, ",\"width\":640,\"height\":480,\"start\":0"))));
Check("positive.tiptapDefaults", () => Valid(Doc(Node("paragraph", Text("x", Link("/", ",\"target\":\"_blank\",\"rel\":\"noopener noreferrer nofollow\",\"class\":null")), "{\"textAlign\":null}"))));
Check("positive.mediaDefaults", () => Valid(Doc(Image(extra: ",\"title\":null,\"width\":null,\"height\":null"))));
Check("positive.orderedListDefaults", () => Valid(Doc(Node("orderedList", Node("listItem", Paragraph()), "{\"start\":1,\"type\":null}"))));
foreach (var align in new[] { "left", "center", "right" })
    Check("positive.cellAlign." + align, () => Valid(Doc(Table(1, 1, Node("tableCell", Paragraph(), "{\"align\":" + Quote(align) + "}")))));
Check("positive.cellDefaults", () => Valid(Doc(Table(1, 1, Node("tableCell", Paragraph(), "{\"colspan\":1,\"rowspan\":1,\"colwidth\":null,\"align\":null}")))));
Check("positive.literalTextIsData", () => validator.Validate(Doc(Paragraph("<img onerror='alert(1)'>"))).Document?.Document.Root.Children[0].Children[0].Text == "<img onerror='alert(1)'>");
Check("positive.emojiCountsOneRune", () => Valid(Doc(Node("paragraph", "{\"type\":\"text\",\"text\":\"" + string.Concat(Enumerable.Repeat("😀", DocumentLimits.TextNodeRunes)) + "\"}"))));
Check("positive.escapedPair", () => Valid(Doc(Node("paragraph", "{\"type\":\"text\",\"text\":\"\\uD83D\\uDE00\"}"))));

Boundary("bytes", empty + new string(' ', DocumentLimits.Utf8Bytes - Encoding.UTF8.GetByteCount(empty)), empty + new string(' ', DocumentLimits.Utf8Bytes - Encoding.UTF8.GetByteCount(empty) + 1), "json.bytes");
Check("limit.bytesUtf8NotCharacters", () => Invalid(empty + new string('ı', DocumentLimits.Utf8Bytes / 2), "json.bytes", DocumentFailureKind.Limit));
Boundary("depth", Nested(29), Nested(30), "document.depth");
Boundary("nodes", Doc(Repeat(Paragraph(), 9999)), Doc(Repeat(Paragraph(), 10000)), "document.nodes");
Boundary("marksPerText", Doc(Node("paragraph", Text("x", allMarks))), Doc(Node("paragraph", Text("x", allMarks + "," + Mark("bold")))), "marks.node");
Boundary("marksTotal", Doc(Node("paragraph", Repeat(Text("x", allMarks), 6000))), Doc(Node("paragraph", Repeat(Text("x", allMarks), 6000) + "," + Text("x", Mark("bold")))), "marks.total");
Boundary("textNode", Doc(Paragraph(new string('ı', 20000))), Doc(Paragraph(new string('ı', 20001))), "text.node");
Boundary("textTotal", Doc(Repeat(Paragraph(new string('x', 20000)), 10)), Doc(Repeat(Paragraph(new string('x', 20000)), 10) + "," + Paragraph("x")), "text.total");
var urlPrefix = "https://example.test/";
Boundary("url", Doc(Node("paragraph", Text("x", Link(urlPrefix + new string('x', 2048 - urlPrefix.Length))))), Doc(Node("paragraph", Text("x", Link(urlPrefix + new string('x', 2049 - urlPrefix.Length))))), "attribute.urlLength");
Boundary("imageUrl", Doc(Image(src: urlPrefix + new string('x', 2048 - urlPrefix.Length))), Doc(Image(src: urlPrefix + new string('x', 2049 - urlPrefix.Length))), "attribute.urlLength");
Boundary("alt", Doc(Image(new string('ı', 300))), Doc(Image(new string('ı', 301))), "attribute.label");
Boundary("imageTitle", Doc(Image(extra: ",\"title\":" + Quote(new string('x', 300)))), Doc(Image(extra: ",\"title\":" + Quote(new string('x', 301)))), "attribute.label");
Boundary("linkTitle", Doc(Node("paragraph", Text("x", Link("/", ",\"title\":" + Quote(new string('x', 300)))))), Doc(Node("paragraph", Text("x", Link("/", ",\"title\":" + Quote(new string('x', 301)))))), "attribute.label");
Boundary("rows", Doc(Table(20, 1)), Doc(Table(21, 1)), "table.rows");
Boundary("columns", Doc(Table(1, 10)), Doc(Table(1, 11)), "table.columns");
Boundary("cells", Doc(Repeat(Table(20, 10), 5)), Doc(Repeat(Table(20, 10), 5) + "," + Table(1, 1)), "document.cells");
Boundary("images", Doc(Repeat(Image(), 50)), Doc(Repeat(Image(), 51)), "document.images");
Boundary("videos", Doc(Repeat(Video(), 10)), Doc(Repeat(Video(), 11)), "document.videos");
Boundary("orderedListStart", Doc(Node("orderedList", Node("listItem", Paragraph()), "{\"start\":10000}")), Doc(Node("orderedList", Node("listItem", Paragraph()), "{\"start\":10001}")), "list.start");

var invalidSchemas = new Dictionary<string, string> {
    ["version"] = empty.Replace("\"version\":1", "\"version\":2"),
    ["missingVersion"] = "{\"document\":" + Node("doc", Paragraph()) + "}",
    ["versionType"] = empty.Replace("\"version\":1", "\"version\":\"1\""),
    ["envelopeField"] = empty.Insert(1, "\"hidden\":1,"),
    ["rootType"] = "{\"version\":1,\"document\":" + Paragraph() + "}",
    ["rootArray"] = "[]",
    ["nullRoot"] = "null",
    ["emptyDoc"] = Doc(""),
    ["unknownNode"] = Doc(Node("html", attrs: "{\"html\":\"<script>alert(1)</script>\"}")),
    ["iframe"] = Doc(Node("iframe")),
    ["unknownMark"] = Doc(Node("paragraph", Text("x", Mark("strike")))),
    ["unknownAttr"] = Doc(Node("paragraph", Text("x"), "{\"onclick\":\"alert(1)\"}")),
    ["unknownField"] = Doc("{\"type\":\"paragraph\",\"hidden\":true}"),
    ["css"] = Doc(Node("paragraph", Text("x"), "{\"style\":\"color:red\"}")),
    ["alignment"] = Doc(Node("paragraph", Text("x"), "{\"textAlign\":\"justify\"}")),
    ["headingH1"] = Doc(Node("heading", Text("x"), "{\"level\":1}")),
    ["headingH5"] = Doc(Node("heading", Text("x"), "{\"level\":5}")),
    ["headingFraction"] = Doc(Node("heading", Text("x"), "{\"level\":2.5}")),
    ["headingMissingLevel"] = Doc(Node("heading", Text("x"))),
    ["attrsNull"] = Doc(Node("paragraph", attrs: "null")),
    ["textDirectlyInDoc"] = Doc(Text("x")),
    ["nestedDoc"] = Doc(Node("doc", Paragraph())),
    ["nestedParagraph"] = Doc(Node("paragraph", Paragraph())),
    ["listWithoutItem"] = Doc(Node("bulletList", Paragraph())),
    ["itemWithoutFirstParagraph"] = Doc(Node("bulletList", Node("listItem", Node("codeBlock")))),
    ["listStartZero"] = Doc(Node("orderedList", Node("listItem", Paragraph()), "{\"start\":0}")),
    ["listType"] = Doc(Node("orderedList", Node("listItem", Paragraph()), "{\"type\":\"a\"}")),
    ["textEmpty"] = Doc(Node("paragraph", Text(""))),
    ["textNull"] = Doc(Node("paragraph", "{\"type\":\"text\",\"text\":null}")),
    ["textChildren"] = Doc(Node("paragraph", "{\"type\":\"text\",\"text\":\"x\",\"content\":[]}")),
    ["textControl"] = Doc(Paragraph("a\0b")),
    ["nonArrayContent"] = Doc("{\"type\":\"paragraph\",\"content\":{}}"),
    ["primitiveChild"] = Doc(Node("paragraph", "5")),
    ["blockMarks"] = Doc("{\"type\":\"paragraph\",\"marks\":[]}"),
    ["blockText"] = Doc("{\"type\":\"paragraph\",\"text\":\"x\"}"),
    ["markNull"] = Doc(Node("paragraph", Text("x", "null"))),
    ["markAttr"] = Doc(Node("paragraph", Text("x", "{\"type\":\"bold\",\"attrs\":{\"color\":\"red\"}}"))),
    ["markDuplicate"] = Doc(Node("paragraph", Text("x", Mark("bold") + "," + Mark("bold")))),
    ["codeMarks"] = Doc(Node("codeBlock", Text("x", Mark("bold")))),
    ["codeParagraph"] = Doc(Node("codeBlock", Paragraph())),
    ["codeLanguage"] = Doc(Node("codeBlock", Text("x"), "{\"language\":\"x onclick=alert(1)\"}")),
    ["imageMissingSource"] = Doc(Node("image")),
    ["imageEvent"] = Doc(Image(extra: ",\"onerror\":\"alert(1)\"")),
    ["imageDimensions"] = Doc(Image(extra: ",\"width\":100")),
    ["imageHeight"] = Doc(Image(extra: ",\"height\":100")),
    ["imageChildren"] = Doc(Node("image", "", "{\"src\":\"https://example.test/a\"}")),
    ["altControl"] = Doc(Image("a\r\nb")),
    ["linkTarget"] = Doc(Node("paragraph", Text("x", Link("/", ",\"target\":\"arbitrary\"")))),
    ["linkRel"] = Doc(Node("paragraph", Text("x", Link("/", ",\"rel\":\"opener\"")))),
    ["linkClass"] = Doc(Node("paragraph", Text("x", Link("/", ",\"class\":\"unsafe\"")))),
    ["tableEmpty"] = Doc(Table(0, 1)),
    ["tableRowEmpty"] = Doc(Table(1, 0)),
    ["tableCellEmpty"] = Doc(Table(1, 1, Node("tableCell"))),
    ["tableMerged"] = Doc(Table(1, 1, Node("tableCell", Paragraph(), "{\"colspan\":2}"))),
    ["tableRowSpan"] = Doc(Table(1, 1, Node("tableCell", Paragraph(), "{\"rowspan\":2}"))),
    ["tableResize"] = Doc(Table(1, 1, Node("tableCell", Paragraph(), "{\"colwidth\":[100]}"))),
    ["tableAlignment"] = Doc(Table(1, 1, Node("tableCell", Paragraph(), "{\"align\":\"justify\"}"))),
    ["tableRagged"] = Doc(Node("table", Node("tableRow", Node("tableCell", Paragraph())) + "," + Node("tableRow", Repeat(Node("tableCell", Paragraph()), 2)))),
    ["tableNested"] = Doc(Table(1, 1, Node("tableCell", Node("blockquote", Table(1, 1))))),
    ["duplicateKey"] = empty.Replace("\"version\":1", "\"version\":1,\"version\":1"),
    ["escapedDuplicateKey"] = empty.Replace("\"version\":1", "\"version\":1,\"ver\\u0073ion\":1"),
    ["nestedDuplicateKey"] = Doc("{\"type\":\"paragraph\",\"type\":\"paragraph\"}"),
    ["trailingComma"] = empty.Insert(empty.Length - 1, ","),
    ["comment"] = empty.Insert(1, "/* x */"),
    ["multipleRoots"] = empty + empty,
    ["truncated"] = empty[..^1],
    ["highSurrogate"] = Doc(Node("paragraph", "{\"type\":\"text\",\"text\":\"\\uD800\"}")),
    ["lowSurrogate"] = Doc(Node("paragraph", "{\"type\":\"text\",\"text\":\"\\uDC00\"}")),
    ["utf16String"] = Doc(Paragraph()).Insert(1, "\ud800"),
    ["syntaxDepth"] = "{\"version\":1,\"document\":" + new string('[', 73) + "0" + new string(']', 73) + "}"
};
foreach (var (name, fixture) in invalidSchemas) Check("reject.schema." + name, () => Invalid(fixture));

var unsafeUrls = new[] { "javascript:alert(1)", "data:image/png;base64,x", "vbscript:x", "http://example.test/", "//example.test/", "relative/path",
    "https://user:pass@example.test/", "https://example.test/\\evil", "https://example.test/\r\nx", " https://example.test/",
    "https://example.test/%00", "https://example.test/%0a", "https://example.test/%5C", "https://example.test/%250a", "https://example.test/%QZ",
    "https://example.test/<script>", "javascript&#58;alert(1)", "javascript%3Aalert(1)", "/%2fexample.test", "/%5cexample.test", "/%252fexample.test", "#" };
for (var i = 0; i < unsafeUrls.Length; i++)
{
    var url = unsafeUrls[i];
    Check("reject.url." + i, () => Invalid(Doc(Node("paragraph", Text("x", Link(url))))));
    Check("reject.imageUrl." + i, () => Invalid(Doc(Image(src: url))));
}
Check("reject.relativeImage", () => Invalid(Doc(Image(src: "/image.gif"))));
var unsafeVideos = new[] { "https://evil.test/embed/abcdefghijk", "https://youtube.com.evil.test/watch?v=abcdefghijk", "https://youtu.be/short",
    "https://youtu.be/abcdefghij!", "https://youtu.be/abcdefghijk/extra", "https://youtu.be/abcdefghijk?autoplay=1", "https://youtu.be/abcdefghijk#x",
    "https://youtu.be:444/abcdefghijk", "https://www.youtube.com/watch?v=abcdefghijk&autoplay=1", "https://www.youtube.com/shorts/abcdefghijk", "https://youtu.be/%61bcdefghijk",
    "https://youtu.be/x/../abcdefghijk", "https://youtu.be:443/abcdefghijk" };
for (var i = 0; i < unsafeVideos.Length; i++)
{
    var url = unsafeVideos[i];
    Check("reject.youtube." + i, () => Invalid(Doc(Video(url))));
}
Check("reject.youtubeOptions", () => Invalid(Doc(Video(extra: ",\"start\":1"))) && Invalid(Doc(Video(extra: ",\"width\":800"))) && Invalid(Doc(Video(extra: ",\"height\":800"))));
Check("reject.invalidUtf8", () => {
    var result = validator.Validate(new byte[] { 0x7b, 0xc0, 0xaf, 0x7d });
    return !result.IsValid && result.Document is null && result.Error?.Code == "json.unicode";
});
Check("reject.byteEntryOverLimit", () => validator.Validate(new byte[DocumentLimits.Utf8Bytes + 1]).Error?.Code == "json.bytes");
Check("contract.stringByteParity", () => {
    var input = Doc(Paragraph("Türkçe 😀"));
    var a = validator.Validate(input); var b = validator.Validate(Encoding.UTF8.GetBytes(input));
    return a.Document?.Document.Root.Children[0].Children[0].Text == b.Document?.Document.Root.Children[0].Children[0].Text && a.IsValid && b.IsValid;
});
Check("contract.failureNoPartialOrPayload", () => {
    var result = validator.Validate(Doc(Paragraph("PRIVATE-SENTINEL") + "," + Node("html")));
    return result.Document is null && result.Error is not null && !JsonSerializer.Serialize(result).Contains("PRIVATE-SENTINEL", StringComparison.Ordinal);
});
Check("contract.resultHasNoPublicConstruction", () => typeof(ValidatedContentDocument).GetConstructors().Length == 0 && typeof(DocumentValidationResult).GetConstructors().Length == 0);
Check("contract.requestStateIsolated", () => {
    var outcomes = new bool[64];
    Parallel.For(0, outcomes.Length, i => outcomes[i] = i % 2 == 0 ? Valid(empty) : Invalid(Doc(Repeat(Image(), 51)), "document.images"));
    return outcomes.All(x => x);
});

DocumentReadingChecks.Run(Check);

var passed = checks.Values.All(value => value);
Console.WriteLine(JsonSerializer.Serialize(new { passed, count = checks.Count, checks, failures }, new JsonSerializerOptions { WriteIndented = true }));
return passed ? 0 : 1;
