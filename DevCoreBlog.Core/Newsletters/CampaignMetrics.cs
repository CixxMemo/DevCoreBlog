using DevCoreBlog.Core.Email;

namespace DevCoreBlog.Core.Newsletters;

/// <summary>Aggregate-only campaign observations; no recipient, payload or credential leaves persistence.</summary>
public sealed record CampaignMetricIdentity(Guid Id, string Subject, NewsletterCampaignState State);
public sealed record CampaignMetricJobs(CampaignDeliveryState State, long Count, long Retrying, long UncertainHeld);
public sealed record CampaignMetricAttempts(EmailSendOutcome? Outcome, long Count);
public sealed record CampaignProviderFacts(long Delivered, long Bounced, long Complained, long TemporaryBounce);
public sealed record CampaignMetrics(CampaignMetricIdentity Campaign, RecipientSelectionState? Selection,
    long Selected, CampaignDispatchSummary? Dispatch, IReadOnlyList<CampaignMetricJobs> Jobs,
    IReadOnlyList<CampaignMetricAttempts> Attempts, CampaignProviderFacts Provider)
{
    public long JobCount(CampaignDeliveryState state) => Jobs.FirstOrDefault(x => x.State == state)?.Count ?? 0;
    public long AttemptCount(EmailSendOutcome? outcome) => Attempts.FirstOrDefault(x => x.Outcome == outcome)?.Count ?? 0;
    public long TotalJobs => Jobs.Sum(x => x.Count);
    public long TotalAttempts => Attempts.Sum(x => x.Count);
    public long DeliveryUnknown => JobCount(CampaignDeliveryState.Accepted) - Provider.Delivered;
}

/// <summary>One read-only consistent observation, distinct from worker writes and infrastructure health probes.</summary>
public interface ICampaignMetricsStore
{
    Task<CampaignMetrics?> ReadAsync(Guid campaignId, CancellationToken cancellationToken);
}
