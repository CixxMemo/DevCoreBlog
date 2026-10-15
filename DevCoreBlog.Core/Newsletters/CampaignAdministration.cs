namespace DevCoreBlog.Core.Newsletters;

/// <summary>Private metadata only; neither list nor counters read bodies, encrypted messages or subscriber addresses.</summary>
public sealed record CampaignSourceSummary(int PostId, long Revision, string Subject);
public sealed record CampaignSummary(Guid Id, int PostId, long SourceRevision, string Subject, NewsletterCampaignState State,
    long Version, DateTime CreatedUtc);
public sealed record CampaignListPage(IReadOnlyList<CampaignSummary> Items, int Page, int TotalCount)
{
    public const int PageSize = 25;
    public const int MaximumPage = 1000;
}
public sealed record CampaignControl(Guid Id, long Version, NewsletterCampaignState State, string SourceSlug, string WebVersionUrl, bool HasDispatch);
public sealed record CampaignDispatchSummary(CampaignPreparationState State, long StagedCount, DateTime ExpiresUtc);
public sealed record CampaignJobCount(CampaignDeliveryState State, long Count, long Attempts, long Retrying);
/// <summary>One consistent database observation; text is encoded by Razor and never treated as trusted HTML.</summary>
public sealed record CampaignDetail(CampaignSummary Summary, string Text, string SenderEmail, string SenderName, string? ReplyTo,
    string WebVersionUrl, string SourceSlug, RecipientSelectionProgress? Selection, CampaignDispatchSummary? Dispatch,
    IReadOnlyList<CampaignJobCount> Jobs)
{
    public long Count(CampaignDeliveryState state) => Jobs.FirstOrDefault(x => x.State == state)?.Count ?? 0;
    public override string ToString() => nameof(CampaignDetail);
    public long Attempts => Jobs.Sum(x => x.Attempts);
    public long Retrying => Jobs.Sum(x => x.Retrying);
}
/// <summary>Bounded private administration reads, separate from immutable-intent and worker writes.</summary>
public interface ICampaignAdministrationStore
{
    Task<CampaignSourceSummary?> ReadSourceSummaryAsync(int postId, CancellationToken cancellationToken);
    Task<CampaignListPage> FindAsync(int page, int? postId, CancellationToken cancellationToken);
    Task<CampaignControl?> ReadControlAsync(Guid id, CancellationToken cancellationToken);
    Task<CampaignDetail?> ReadAsync(Guid id, CancellationToken cancellationToken);
}
