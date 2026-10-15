using DevCoreBlog.Core.Newsletters;

namespace DevCoreBlog.Services.Newsletters;

public sealed record CampaignAdministrationOptions(bool WorkerEnabled);
public enum CampaignActionStatus { Ready, Selecting, Existing, Cancelled, Conflict, NotFound, Disabled, Unavailable }
public sealed record CampaignActionResult(CampaignActionStatus Status, Guid? Id = null);

/// <summary>Explicit admin actions compose existing durable boundaries; retry never resets an attempt, payload, key or lifetime.</summary>
public sealed class CampaignAdministrationService(ICampaignAdministrationStore reads,
    NewsletterCampaignService preparation, CampaignRecipientService recipients, CampaignPreparationService dispatch,
    CampaignDeliveryReadiness readiness, CampaignAdministrationOptions options, NewsletterCampaignOrigin origin)
{
    public bool IsReady => options.WorkerEnabled && readiness.IsReady;
    public bool HasCurrentArchiveLink(CampaignDetail detail) => origin.Origin is not null && detail.WebVersionUrl == origin.WebVersion(detail.SourceSlug);
    public async Task<CampaignListPage> FindAsync(int page, int? postId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        return await reads.FindAsync(page, postId, deadline.Token);
    }
    public async Task<CampaignDetail?> ReadAsync(Guid id, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        return await reads.ReadAsync(id, deadline.Token);
    }
    public async Task<CampaignStartResult> PrepareAsync(int postId, long revision, Guid key, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        return await preparation.PrepareAsync(postId, revision, key, deadline.Token);
    }
    public async Task<CampaignSourceSummary?> SourceAsync(int postId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        return await reads.ReadSourceSummaryAsync(postId, deadline.Token);
    }
    public async Task<CampaignActionResult> StartAsync(Guid id, long version, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        try
        {
            var detail = await reads.ReadControlAsync(id, deadline.Token);
            if (detail is null) return new(CampaignActionStatus.NotFound);
            if (detail.Version != version || detail.State != NewsletterCampaignState.Prepared
                || origin.Origin is null || detail.WebVersionUrl != origin.WebVersion(detail.SourceSlug))
                return new(CampaignActionStatus.Conflict, id);
            // Replaying a committed dispatch never touches jobs, including Held, rejected or expired work.
            if (detail.HasDispatch) return new(CampaignActionStatus.Existing, id);
            if (!IsReady) return new(CampaignActionStatus.Disabled, id);
            var batch = await recipients.AdvanceAsync(id, deadline.Token);
            if (batch.Status == RecipientBatchStatus.Advanced) return new(CampaignActionStatus.Selecting, id);
            if (batch.Status == RecipientBatchStatus.Cancelled) return new(CampaignActionStatus.Conflict, id);
            if (batch.Status == RecipientBatchStatus.NotFound) return new(CampaignActionStatus.NotFound);
            if (batch.Status != RecipientBatchStatus.Complete) return new(CampaignActionStatus.Unavailable, id);
            var armed = await dispatch.ArmAsync(id, deadline.Token);
            return new(armed switch
            {
                CampaignArmOutcome.Armed => CampaignActionStatus.Ready,
                CampaignArmOutcome.Existing => CampaignActionStatus.Existing,
                CampaignArmOutcome.NotConfigured => CampaignActionStatus.Disabled,
                CampaignArmOutcome.InvalidState => CampaignActionStatus.Conflict,
                CampaignArmOutcome.NotFound => CampaignActionStatus.NotFound,
                _ => CampaignActionStatus.Unavailable
            }, id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return new(CampaignActionStatus.Unavailable, id); }
    }
    public async Task<CampaignActionResult> CancelAsync(Guid id, long version, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var status = await preparation.CancelAsync(id, version, deadline.Token);
        return new(status switch
        {
            CampaignCancelStatus.Cancelled or CampaignCancelStatus.Unchanged => CampaignActionStatus.Cancelled,
            CampaignCancelStatus.NotFound => CampaignActionStatus.NotFound,
            _ => CampaignActionStatus.Conflict
        }, id);
    }
    private static CancellationTokenSource Deadline(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(TimeSpan.FromSeconds(20));
        return source;
    }
}
