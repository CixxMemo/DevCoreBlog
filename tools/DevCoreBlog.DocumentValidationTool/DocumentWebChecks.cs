using System.Globalization;
using System.Net;
using System.Text.Json;
using DevCoreBlog.Services.Documents;

/// <summary>Synthetic web security and feature regression without a host or external requests.</summary>
internal static class DocumentWebChecks
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        var validator = new ContentDocumentValidator();
        var renderer = new DocumentWebRenderer(new DocumentTextProducer());
        ValidatedContentDocument Validate(string blocks) => validator.Validate(Doc(blocks)).Document
            ?? throw new InvalidOperationException("Invalid synthetic fixture.");
        RenderedContentDocument Render(string blocks) => renderer.Render(Validate(blocks));
        string Html(string blocks) => Render(blocks).Html;
        check("web.outputCannotBeForged", () => typeof(RenderedContentDocument).GetConstructors().Length == 0);
        check("web.emptyParagraph", () => Html(P()) == "<p class=\"document-align-left\"></p>");
        const string literal = "<script>alert('x')</script> & Türkçe 😀 </textarea><img onerror='x'>";
        check("web.literalTextEncoded", () =>
        {
            var html = Html(P(literal));
            return !html.Contains("<script>") && !html.Contains("<img") && html.Contains("&lt;script&gt;")
                && WebUtility.HtmlDecode(html).Contains(literal);
        });
        check("web.hardBreak", () => Html(N("paragraph", T("bir") + "," + N("hardBreak") + "," + T("iki"))).Contains("bir<br>iki"));
        foreach (var (mark, tag) in new[] { ("bold", "strong"), ("italic", "em"), ("underline", "u"), ("code", "code") })
            check("web.mark." + mark, () => Html(N("paragraph", T("x", N(mark)))).Contains("<" + tag + ">x</" + tag + ">"));
        var marks = N("bold") + "," + N("italic") + "," + N("underline") + "," + N("code") + "," + Link("https://example.test/?a=1&b=2", "_blank", "noopener noreferrer nofollow", literal);
        check("web.combinedMarksAndSafeLink", () =>
        {
            var html = Html(N("paragraph", T("etiket", marks)));
            return html.Contains("<strong><em><u><code><a href=\"https://example.test/?a=1&amp;b=2\"")
                && html.Contains("target=\"_blank\" rel=\"noopener noreferrer nofollow\"")
                && html.EndsWith("etiket</a></code></u></em></strong></p>") && !html.Contains("<script>");
        });
        foreach (var href in new[] { "https://example.test/x", "/yerel?x=1&y=2", "#document-section-1" })
            check("web.link." + (href[0] == '#' ? "fragment" : href[0] == '/' ? "local" : "https"), () =>
                Html(N("paragraph", T("x", Link(href)))).Contains("target=\"_self\" rel=\"noopener noreferrer\""));
        foreach (var href in new[] { "javascript:alert(1)", "data:text/html,x", "//evil.test/x", "https://user:pass@example.test/x" })
            check("web.unsafeLinkRejected." + href.Split(':')[0], () => validator.Validate(Doc(N("paragraph", T("x", Link(href))))).Document is null);
        foreach (var alignment in new[] { "left", "center", "right" })
        {
            check("web.paragraphAlignment." + alignment, () => Html(N("paragraph", T("x"), "{\"textAlign\":" + Q(alignment) + "}")).Contains("class=\"document-align-" + alignment + "\""));
            check("web.cellAlignment." + alignment, () => Html(N("table", N("tableRow", N("tableCell", P("x"), "{\"align\":" + Q(alignment) + "}")))).Contains("<td class=\"document-align-" + alignment + "\""));
        }
        var headings = H(literal, 2) + "," + N("blockquote", H("aynı", 3)) + "," + N("table", N("tableRow", N("tableCell", H("aynı", 4)))) + "," + H("", 2);
        check("web.headingTargetsFromSameSnapshot", () =>
        {
            var r = Render(headings);
            return r.Reading.Headings.Length == 4 && r.Reading.Headings[0].Title == literal
                && r.Reading.Headings.All(h => r.Html.Contains("<h" + h.Level + " id=\"" + h.Id + "\""));
        });
        check("web.listsAndBlockquote", () =>
        {
            var html = Html(N("bulletList", N("listItem", P("bir") + "," + N("orderedList", N("listItem", P("iki")), "{\"start\":10000}"))) + "," + N("blockquote", P("alıntı")));
            return html.Contains("<ul><li>") && html.Contains("<ol start=\"10000\"><li>") && html.Contains("<blockquote>");
        });
        foreach (var language in new[] { "plaintext", "csharp", "javascript", "typescript", "json", "python", "bash", "sql", "html", "css", "markdown" })
            check("web.code." + language, () =>
            {
                var html = Html(N("codeBlock", T("<b>\n\t  x\r\n&"), "{\"language\":" + Q(language) + "}"));
                return html.Contains("class=\"language-" + language + "\"") && html.Contains("tabindex=\"0\"")
                    && WebUtility.HtmlDecode(html).Contains("<b>\n\t  x\r\n&") && !html.Contains("<b>");
            });
        check("web.emptyCodeDefault", () => Html(N("codeBlock")).Contains("language-plaintext\"></code></pre>"));
        check("web.imageAltTitleEncodedGifPreserved", () =>
        {
            var html = Html(N("image", attrs: "{\"src\":\"https://example.test/animation.gif?a=1&b=2\",\"alt\":" + Q(literal) + ",\"title\":" + Q(literal) + "}"));
            return html.Contains("animation.gif?a=1&amp;b=2") && html.Contains("alt=\"&lt;script&gt;")
                && html.Contains("loading=\"lazy\" decoding=\"async\"") && !html.Contains("\" onerror=") && !html.Contains("width=");
        });
        // Encoded onerror= inside a label is harmless; the browser also verifies no event attribute exists.
        check("web.decorativeImage", () => Html(N("image", attrs: "{\"src\":\"https://example.test/x.png\"}")).Contains("alt=\"\""));
        foreach (var source in new[] { "https://youtu.be/abcdefghijk", "https://youtube.com/watch?v=abcdefghijk", "https://www.youtube.com/embed/abcdefghijk", "https://www.youtube-nocookie.com/embed/abcdefghijk" })
            check("web.youtube." + source.Split('/')[2], () => Html(N("youtube", attrs: "{\"src\":" + Q(source) + "}"))
                == "<div class=\"document-video\"><iframe src=\"https://www.youtube-nocookie.com/embed/abcdefghijk\" title=\"YouTube videosu\" loading=\"lazy\" referrerpolicy=\"no-referrer\" allow=\"fullscreen\" allowfullscreen></iframe></div>");
        check("web.fakeEmbedRejected", () => validator.Validate(Doc(N("youtube", attrs: "{\"src\":\"https://evil.test/embed/abcdefghijk\"}"))).Document is null);
        check("web.semanticTable", () =>
        {
            var html = Html(N("table", N("tableRow", N("tableHeader", P("ad")) + "," + N("tableHeader", P("değer"))) + "," + N("tableRow", N("tableCell", P("bir")) + "," + N("tableCell", P("iki")))));
            return html.Contains("tabindex=\"0\" role=\"region\"") && html.Contains("<table><tbody><tr><th ") && html.Contains("</th></tr><tr><td ") && html.EndsWith("</tbody></table></div>");
        });
        check("web.maxTableAndLongCode", () =>
        {
            var cells = string.Join(',', Enumerable.Repeat(N("tableCell", P(new string('x', 200))), 10));
            return Html(N("table", string.Join(',', Enumerable.Repeat(N("tableRow", cells), 20))) + "," + N("codeBlock", T(new string('x', 20000)))).Contains(new string('x', 20000));
        });
        check("web.maxTextEncodingExpansion", () => Html(string.Join(',', Enumerable.Repeat(P(new string('<', 20000)), 10)).Replace("\\u003C", "<", StringComparison.Ordinal)).Length > 800000);
        check("web.maxNodes", () => Render(string.Join(',', Enumerable.Repeat(P(), 9999))).Reading.WordCount == 0);
        check("web.maxDepth", () => { var n = P("x"); for (var i = 0; i < 29; i++) n = N("blockquote", n); return Html(n).Contains(">x</p>"); });
        check("web.deterministicNoMutationCulture", () =>
        {
            var d = Validate(headings);
            var before = JsonSerializer.Serialize(d.Document);
            var first = renderer.Render(d).Html;
            var culture = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA"); return renderer.Render(d).Html == first && JsonSerializer.Serialize(d.Document) == before; }
            finally { CultureInfo.CurrentCulture = culture; }
        });
        check("web.parallelIsolation", () =>
        {
            var outcomes = new bool[64];
            Parallel.For(0, outcomes.Length, i => { var r = Render(i % 2 == 0 ? headings : P()); outcomes[i] = r.Reading.Headings.Length == (i % 2 == 0 ? 4 : 0); });
            return outcomes.All(x => x);
        });
        foreach (var (name, invalid) in new[] { ("rawHtml", N("html")), ("style", N("paragraph", T("x"), "{\"style\":\"color:red\"}")), ("imageCredentials", N("image", attrs: "{\"src\":\"https://u:p@example.test/x\"}")), ("codeClass", N("codeBlock", T("x"), "{\"language\":\"x onclick=x\"}")) })
            check("web.rejectedInput." + name, () => validator.Validate(Doc(invalid)).Document is null);
    }

    private static string Q(string value) => JsonSerializer.Serialize(value);
    private static string N(string kind, string? children = null, string? attrs = null) => "{\"type\":" + Q(kind) + (attrs is null ? "" : ",\"attrs\":" + attrs) + (children is null ? "" : ",\"content\":[" + children + "]") + "}";
    private static string T(string text, string? marks = null) => "{\"type\":\"text\",\"text\":" + Q(text) + (marks is null ? "" : ",\"marks\":[" + marks + "]") + "}";
    private static string P(string? text = null) => N("paragraph", text is null ? null : T(text));
    private static string H(string text, int level) => N("heading", text.Length == 0 ? null : T(text), "{\"level\":" + level + "}");
    private static string Link(string href, string? target = null, string? rel = null, string? title = null) => N("link", attrs: JsonSerializer.Serialize(new { href, target, rel, title }));
    private static string Doc(string blocks) => "{\"version\":1,\"document\":" + N("doc", blocks) + "}";
}
