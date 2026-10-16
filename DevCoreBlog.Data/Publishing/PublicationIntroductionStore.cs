using DevCoreBlog.Core.Publishing;
using Microsoft.EntityFrameworkCore;

namespace DevCoreBlog.Data.Publishing;

/// <summary>Reads only the two public text columns under bounded, read-only storage access.</summary>
public sealed class PublicationIntroductionStore(ApplicationDbContext db) : IPublicationIntroductionStore
{
    public async Task<PublicationIntroduction?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='5s'; SET TRANSACTION READ ONLY", cancellationToken);
        var introduction = await db.PublicationSettings.AsNoTracking()
            .Select(settings => new PublicationIntroduction(settings.Name, settings.Cadence))
            .SingleOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return introduction;
    }
}
