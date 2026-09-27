namespace DevCoreBlog.Services.Publishing;

/// <summary>Exposes only the next public publication boundary to cache policy.</summary>
public interface IPublicationSchedule
{
    Task<DateTime?> GetNextScheduledPublicationAsync(
        DateTime utcNow, CancellationToken cancellationToken = default);
}
