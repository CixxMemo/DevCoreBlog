using DevCoreBlog.Core.Newsletters;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Internal preparation only. Bounded I/O failure never becomes an empty or complete recipient set.</summary>
public sealed class CampaignRecipientService(ICampaignRecipientStore store, TimeProvider clock)
{
    public async Task<RecipientBatchResult> AdvanceAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (campaignId == Guid.Empty) return new(RecipientBatchStatus.Invalid);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try { return await store.AdvanceAsync(campaignId, clock.GetUtcNow().UtcDateTime, deadline.Token); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(RecipientBatchStatus.Unavailable); }
    }
}
