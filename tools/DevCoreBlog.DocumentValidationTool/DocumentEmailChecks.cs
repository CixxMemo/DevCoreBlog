using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Services.Documents;

/// <summary>Email fixture matrix exercises the trust capability and output without DB, HTTP or delivery.</summary>
internal static class DocumentEmailChecks
{
    private const string Literal = "<script>window.__emailXss=1</script><img src=x onerror=\"window.__emailXss=2\"> Türkçe ıİğĞşŞöÖçÇüÜ 😀 &";
    private static readonly EmailRenderContext Context = EmailRenderContext.Create("https://example.test/post/sayi", "https://example.test/unsubscribe?t=synthetic&a=1");
    private static readonly DocumentEmailRenderer Renderer = new(new DocumentTextProducer());
    private static ValidatedContentDocument Validate(string blocks) => new ContentDocumentValidator().Validate(Doc(blocks)).Document
        ?? throw new InvalidOperationException("Invalid synthetic email fixture.");
    private static RenderedEmailDocument Render(string blocks, EmailRenderContext? context = null) => Renderer.Render(Validate(blocks), context ?? Context);

    internal static void Run(Action<string, Func<bool>> check)
    {
        check("email.noPublicOutputOrContextConstruction", () => typeof(RenderedEmailDocument).GetConstructors().Length == 0 && typeof(EmailRenderContext).GetConstructors().Length == 0);
        check("email.nullDocumentRejected", () => { try { Renderer.Render(null!, Context); return false; } catch (ArgumentNullException) { return true; } });
        check("email.emptyNoInventedFooterOrLanguage", () => { var r=Render(P(), EmailRenderContext.Create("https://example.test/p"));return r.PlainText=="" && !r.Html.Contains("Abonelikten") && !r.Html.Contains("lang="); });
        check("email.literalEncodedAndTextExact", () => { var r=Render(P(Literal));return !r.Html.Contains("<script>") && !r.Html.Contains("<img src=x") && WebUtility.HtmlDecode(r.Html).Contains(Literal) && r.PlainText.StartsWith(Literal, StringComparison.Ordinal); });
        foreach (var (mark,tag) in new[] {("bold","strong"),("italic","em"),("underline","u"),("code","code")})
            check("email.mark."+mark, () => Render(N("paragraph",T("biçim",N(mark)))).Html.Contains("<"+tag+">bi") && Render(N("paragraph",T("biçim",N(mark)))).PlainText.StartsWith("biçim"));
        foreach (var (href,resolved,name) in new[] {("https://outside.test/?x=1&y=2","https://outside.test/?x=1&y=2","https"),("/yerel?q=1&x=2","https://example.test/yerel?q=1&x=2","root"),("#document-section-1","https://example.test/post/sayi#document-section-1","fragment")})
            check("email.link."+name,()=>{var r=Render(N("paragraph",T("bağlantı",Link(href))));return WebUtility.HtmlDecode(r.Html).Contains("href=\""+resolved+"\"") && r.PlainText.Contains("bağlantı ("+resolved+")");});
        check("email.linkTitleEncodingAndRel",()=>{var r=Render(N("paragraph",T("x",Link("https://outside.test/",Literal,"noopener noreferrer nofollow"))));return r.Html.Contains("rel=\"noopener noreferrer nofollow\"") && !r.Html.Contains("<script>") && r.Html.Contains("title=\"&lt;script&gt;");});
        check("email.hardBreak",()=>Render(N("paragraph",T("bir")+","+N("hardBreak")+","+T("iki"))).PlainText.StartsWith("bir\niki"));
        foreach(var align in new[]{"left","center","right"})
            check("email.alignment."+align,()=>Render(N("paragraph",T("x"),"{\"textAlign\":"+Q(align)+"}")).Html.Contains("text-align:"+align+";"));
        foreach(var level in new[]{2,3,4})
            check("email.heading."+level,()=>{var r=Render(N("heading",T("başlık"),"{\"level\":"+level+"}"));return r.Html.Contains("<h"+level+" id=\"document-section-1\"") && r.Reading.Headings.Single().Level==level && r.PlainText.StartsWith("başlık");});
        check("email.nestedListsAndStart",()=>{var r=Render(N("bulletList",N("listItem",P("bir")+","+N("orderedList",N("listItem",P("iki"))+","+N("listItem",P("üç")),"{\"start\":7}"))));return r.Html.Contains("<ol start=\"7\">") && r.PlainText.Contains("- bir\n  \n  7. iki\n  8. üç");});
        check("email.blockquote",()=>Render(N("blockquote",P("alıntı"))).Html.Contains("<blockquote>") && Render(N("blockquote",P("alıntı"))).PlainText.StartsWith("alıntı"));
        foreach(var language in new[]{"plaintext","csharp","javascript","typescript","json","python","bash","sql","html","css","markdown"})
            check("email.code."+language,()=>{const string code="\n\t<code> & 'x'\r\n  son\n";var r=Render(N("codeBlock",T(code),"{\"language\":"+Q(language)+"}"));return WebUtility.HtmlDecode(r.Html).Contains(code) && r.PlainText.StartsWith(code) && r.Html.Contains("white-space:pre-wrap") && !r.Html.Contains("<code> &");});
        check("email.gifAltTitleAndNoLazy",()=>{var r=Render(N("image",attrs:"{\"src\":\"https://example.test/test.gif?x=1&y=2\",\"alt\":"+Q(Literal)+",\"title\":"+Q(Literal)+"}"));return r.Html.Contains("test.gif?x=1&amp;y=2") && r.Html.Contains("max-width:100%") && !r.Html.Contains("loading=") && !r.Html.Contains("\" onerror=") && r.PlainText.Contains(Literal+": https://example.test/test.gif?x=1&y=2");});
        check("email.decorativeImage",()=>Render(N("image",attrs:"{\"src\":\"https://example.test/x.png\"}")).Html.Contains("alt=\"\""));
        foreach(var src in new[]{"https://youtu.be/abcdefghijk","https://youtube.com/watch?v=abcdefghijk","https://www.youtube.com/embed/abcdefghijk","https://www.youtube-nocookie.com/embed/abcdefghijk"})
            check("email.youtube."+src.Split('/')[2],()=>{var r=Render(N("youtube",attrs:"{\"src\":"+Q(src)+"}"));return r.Html.Contains("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg") && r.Html.Contains("https://www.youtube.com/watch?v=abcdefghijk") && !r.Html.Contains("iframe") && r.PlainText.Contains("https://www.youtube.com/watch?v=abcdefghijk");});
        check("email.semanticTableAndText",()=>{var r=Render(Table());return r.Html.Contains("<table style=") && r.Html.Contains("<th style=") && r.Html.Contains("<td style=") && r.Html.Contains("table-layout:fixed") && r.PlainText.Contains("Ad | Değer\nbir | iki");});
        check("email.footerControlledEncoded",()=>{var r=Render(P("x"));return r.Html.Contains("unsubscribe?t=synthetic&amp;a=1") && r.PlainText.EndsWith("Abonelikten çık: https://example.test/unsubscribe?t=synthetic&a=1");});
        check("email.readingMatchesWeb",()=>{var d=Validate(Representative());var e=Renderer.Render(d,Context);var w=new DocumentWebRenderer(new DocumentTextProducer()).Render(d);return JsonSerializer.Serialize(e.Reading)==JsonSerializer.Serialize(w.Reading);});
        check("email.noExecutableSurface",()=>{var h=Render(Representative()).Html;return !h.Contains("<script") && !h.Contains("<iframe") && !h.Contains("<style") && !h.Contains("<form") && !h.Contains("javascript:") && !h.Contains(" class=");});
        var invalidUrlIndex=0;
        foreach(var url in new[]{"http://example.test/p","javascript:x","//example.test/p","https://u:p@example.test/p","https://example.test/%0Aprivate","https://example.test/%5cprivate","https://example.test/%zz","https://example.test/\ud800", "https://example.test/"+new string('x',2048)})
            check("email.badContext."+invalidUrlIndex++,()=>{try{EmailRenderContext.Create(url);return false;}catch(ArgumentException e){return !e.Message.Contains("private");}});
        check("email.crossOriginExitRejected",()=>{try{EmailRenderContext.Create("https://example.test/p","https://evil.test/token");return false;}catch(ArgumentException e){return !e.Message.Contains("token");}});
        check("email.portMismatchExitRejected",()=>{try{EmailRenderContext.Create("https://example.test/p","https://example.test:444/token");return false;}catch(ArgumentException){return true;}});
        var invalidBlockIndex=0;
        foreach(var block in new[]{N("html"),N("paragraph",T("x"),"{\"style\":\"color:red\"}"),N("image",attrs:"{\"src\":\"javascript:x\"}"),N("youtube",attrs:"{\"src\":\"https://evil.test/embed/abcdefghijk\"}")})
            check("email.untrustedRejected."+invalidBlockIndex++,()=>new ContentDocumentValidator().Validate(Doc(block)).Document is null);
        check("email.maxEncodedTextBudget",()=>{var r=Render(string.Join(',',Enumerable.Repeat(P(new string('<',20000)),10)).Replace("\\u003C","<",StringComparison.Ordinal));return Encoding.UTF8.GetByteCount(r.Html)<=DocumentEmailRenderer.HtmlUtf8Bytes && r.PlainText.Count(c=>c=='<')==200000;});
        check("email.maxNodes",()=>Render(string.Join(',',Enumerable.Repeat(P(),9999))).Reading.WordCount==0);
        check("email.maxTableLongCode",()=>{var row=N("tableRow",string.Join(',',Enumerable.Repeat(N("tableCell",P(new string('x',200))),10)));var r=Render(N("table",string.Join(',',Enumerable.Repeat(row,20)))+","+N("codeBlock",T(new string('y',20000))));return r.PlainText.Contains(new string('y',20000)) && r.Html.Contains(new string('y',20000));});
        check("email.deterministicCultureNoMutation",()=>{var d=Validate(Representative());var before=JsonSerializer.Serialize(d.Document);var r=Renderer.Render(d,Context);var culture=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("ar-SA");var after=Renderer.Render(d,Context);return r.Html==after.Html && r.PlainText==after.PlainText && JsonSerializer.Serialize(d.Document)==before;}finally{CultureInfo.CurrentCulture=culture;}});
        check("email.parallelIsolation",()=>{var ok=new bool[32];Parallel.For(0,ok.Length,i=>{var r=Render(i%2==0?Representative():P("other"));ok[i]=r.Html.Contains("<table")== (i%2==0);});return ok.All(v=>v);});
    }

    internal static void Export(string directory)
    {
        if (Directory.Exists(directory)) throw new InvalidOperationException("Fixture destination must be new.");
        Directory.CreateDirectory(directory);
        var r=Render(Representative());
        File.WriteAllText(Path.Combine(directory,"representative.html"),r.Html);
        File.WriteAllText(Path.Combine(directory,"representative.txt"),r.PlainText);
        File.WriteAllText(Path.Combine(directory,"representative.json"),Doc(Representative()));
    }
    private static string Representative()=>N("heading",T("Türkçe başlık 😀"),"{\"level\":2}")+","+P(Literal)+","+N("paragraph",T("Biçimli bağlantı",N("bold")+","+N("italic")+","+N("underline")+","+N("code")+","+Link("/deney?a=1&b=2",Literal)))+","+N("blockquote",P("Alıntı"))+","+N("bulletList",N("listItem",P("Birinci")))+","+N("orderedList",N("listItem",P("Sıralı")),"{\"start\":7}")+","+N("codeBlock",T("\tconst örnek = \"<script>\";\n"+new string('x',18000)),"{\"language\":\"javascript\"}")+","+Table()+","+N("image",attrs:"{\"src\":\"https://example.test/fixture.gif\",\"alt\":"+Q(Literal)+"}")+","+N("youtube",attrs:"{\"src\":\"https://youtu.be/abcdefghijk\"}");
    private static string Table()=>N("table",N("tableRow",N("tableHeader",P("Ad"))+","+N("tableHeader",P("Değer")))+","+N("tableRow",N("tableCell",P("bir"))+","+N("tableCell",P("iki"))));
    private static string Q(string value)=>JsonSerializer.Serialize(value);
    private static string N(string kind,string? children=null,string? attrs=null)=>"{\"type\":"+Q(kind)+(attrs is null?"":",\"attrs\":"+attrs)+(children is null?"":",\"content\":["+children+"]")+"}";
    private static string T(string value,string? marks=null)=>"{\"type\":\"text\",\"text\":"+Q(value)+(marks is null?"":",\"marks\":["+marks+"]")+"}";
    private static string P(string? value=null)=>N("paragraph",value is null?null:T(value));
    private static string Link(string href,string? title=null,string? rel=null)=>N("link",attrs:JsonSerializer.Serialize(new{href,title,rel}));
    private static string Doc(string blocks)=>"{\"version\":1,\"document\":"+N("doc",blocks)+"}";
}
