using DevCoreBlog.Core.Documents;
using DevCoreBlog.Services.Documents;
using DevCoreBlog.Services.Publishing;

namespace DevCoreBlog.Controllers;

/// <summary>Routes public-article writing through the shared private form boundary.</summary>
public sealed class AdminWritingController(PostWritingService writing, WritingCategoryService categories,
    PublicationTimeZone zone, TimeProvider clock, ContentDocumentValidator validator)
    : DocumentWritingControllerBase(writing, categories, zone, clock, validator)
{
    protected override bool IsNewsletter => false;
}
