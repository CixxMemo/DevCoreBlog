using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Publishing;
using DevCoreBlog.Services.Documents;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Owns newsletter scope and private inventory while reusing canonical writing/publication rules.</summary>
public sealed class NewsletterWritingService(PostWritingService writing, INewsletterInventory inventory,
    TimeProvider clock) : IPostWritingUseCase
{
    public Task<EditorPostRead?> ReadAsync(int id, CancellationToken cancellationToken) =>
        writing.ReadAsync(id, WritingPurpose.Newsletter, cancellationToken);
    public Task<EditorPostSave> SaveAsync(EditorPostInput input, CancellationToken cancellationToken) =>
        writing.SaveAsync(input, WritingPurpose.Newsletter, cancellationToken);

    public async Task<NewsletterListPage> FindPageAsync(int page, CancellationToken cancellationToken)
    {
        var result = await inventory.FindPageAsync(page, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        return new(result.Items.Select(item => new NewsletterListItem(item,
            PostPublication.StateAt(item.IsActive, item.CategoryActive, item.IsPublished, item.PublishDate, now))).ToArray(),
            result.TotalCount, page);
    }
}
