using DevCoreBlog.Core.Documents;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace DevCoreBlog.Data.Repositories;

/// <summary>Fail closed when a dropdown cannot represent all active category choices within its bound.</summary>
public sealed class WritingCategoryChoicesRepository(ApplicationDbContext context) : IWritingCategoryChoices
{
    public async Task<WritingCategoryChoices> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var items = await context.Categories.AsNoTracking().Where(category => category.IsActive)
                .OrderBy(category => category.Name).ThenBy(category => category.Id).Take(257)
                .Select(category => new WritingCategoryOption(category.Id, category.Name)).ToArrayAsync(cancellationToken);
            return items.Length > 256 ? new([], false) : new(items, true);
        }
        catch (DbException) { throw new EditorPostStorageException(); }
    }
}
