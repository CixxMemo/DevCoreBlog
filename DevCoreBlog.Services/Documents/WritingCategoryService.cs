using DevCoreBlog.Core.Documents;

namespace DevCoreBlog.Services.Documents;

/// <summary>Exposes bounded writing choices through a use-case boundary rather than controller persistence access.</summary>
public sealed class WritingCategoryService(IWritingCategoryChoices choices)
{
    public Task<WritingCategoryChoices> ReadAsync(CancellationToken cancellationToken) => choices.ReadAsync(cancellationToken);
}
