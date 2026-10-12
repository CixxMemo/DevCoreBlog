using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Rendering;

/// <summary>Exercises fail-closed public capabilities, stored fact tampering and the shared eligibility policy.</summary>
internal static class PublicContentChecks
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        var validator = new ContentDocumentValidator(); var text = new DocumentTextProducer();
        var renderer = new DocumentWebRenderer(text);
        var service = new PublicPostContentService(validator, text, renderer, new SafeMarkdownRenderer(), TimeProvider.System);
        const string json = "{\"version\":1,\"document\":{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":2},\"content\":[{\"type\":\"text\",\"text\":\"Türkçe başlık\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Korunan gövde\"}]}]}}";
        var admitted = validator.Validate(json).Document ?? throw new InvalidOperationException("Fixture invalid.");
        var reading = text.Produce(admitted);
        Post Post() => new() { Id=1, Title="Fixture", Content="Legacy decoy", IsActive=true, IsPublished=true,
            Category=new Category { IsActive=true }, PublishDate=DateTime.UnixEpoch, EditVersion=7,
            DocumentVersion=1, DocumentJson=json, DocumentPlainText=reading.PlainText,
            DocumentWordCount=reading.WordCount, DocumentReadingMinutes=reading.ReadingMinutes };
        check("public.sameValidatedRendererAndReading", () => {
            var post=Post();var output=service.Render(post);
            return output?.Document is { } document && document.Html==renderer.Render(admitted).Html
                && document.Reading.PlainText==reading.PlainText && document.Reading.WordCount==reading.WordCount
                && document.Reading.ReadingMinutes==reading.ReadingMinutes && output.Headings[0].Id==reading.Headings[0].Id && post.EditVersion==7 && post.ViewCount==0;
        });
        var mutations = new (string Name, Action<Post> Change)[] {
            ("plainText", p=>p.DocumentPlainText="Tampered"), ("wordCount", p=>p.DocumentWordCount=999),
            ("readingMinutes", p=>p.DocumentReadingMinutes=999), ("unknownVersion", p=>p.DocumentVersion=2),
            ("invalidJson", p=>p.DocumentJson="{"), ("partialLegacy", p=>p.DocumentVersion=null),
            ("private", p=>p.AccessScope=PostAccessScope.Subscribers), ("newsletter", p=>p.ContentKind=PostContentKind.Newsletter),
            ("unknownKind", p=>p.ContentKind=(PostContentKind)99), ("inactive", p=>p.IsActive=false),
            ("inactiveCategory", p=>p.Category.IsActive=false), ("draft", p=>p.IsPublished=false),
            ("future", p=>p.PublishDate=DateTime.MaxValue) };
        foreach(var mutation in mutations) check("public.reject."+mutation.Name, ()=>{
            var post=Post();mutation.Change(post);return service.Render(post) is null && post.EditVersion==7;
        });
        check("public.legacyOnlyWithAllDocumentFactsAbsent", ()=>{
            var post=Post();post.DocumentVersion=null;post.DocumentJson=null;post.DocumentPlainText=null;
            post.DocumentWordCount=null;post.DocumentReadingMinutes=null;
            return service.Render(post)?.Markdown is not null && service.Render(post)?.Document is null;
        });
        check("public.kindKeysNeverSelectPrivateOrUnknown", ()=>{
            foreach(var kind in PublicContentFilter.Choices) if(!PublicContentFilter.TryParse(PublicContentFilter.Key(kind),out var actual)||actual!=kind)return false;
            return !PublicContentFilter.TryParse("newsletter",out _) && !PublicContentFilter.TryParse("4",out _) && !PublicContentFilter.TryParse("unknown",out _);
        });
    }
}
