namespace DevCoreBlog.Core.Documents;

/// <summary>Only bounded active category options leave persistence for the shared writing form.</summary>
public interface IWritingCategoryChoices
{
    Task<WritingCategoryChoices> ReadAsync(CancellationToken cancellationToken);
}
public sealed record WritingCategoryOption(int Id, string Name);
public sealed record WritingCategoryChoices(IReadOnlyList<WritingCategoryOption> Items, bool Complete);
