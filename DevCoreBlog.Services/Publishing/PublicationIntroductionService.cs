using DevCoreBlog.Core.Publishing;

namespace DevCoreBlog.Services.Publishing;

/// <summary>Defaults apply only before the first preference record; storage failure remains a failure.</summary>
public sealed class PublicationIntroductionService(IPublicationIntroductionStore store)
{
    public async Task<PublicationIntroduction> ReadAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return await store.ReadAsync(deadline.Token)
            ?? new PublicationIntroduction(PublicationSettings.DefaultName, PublicationSettings.DefaultCadence);
    }
}
