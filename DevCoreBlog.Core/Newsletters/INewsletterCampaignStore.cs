namespace DevCoreBlog.Core.Newsletters;

/// <summary>Private canonical source projection; unrelated counters, publication time and entity graphs are excluded.</summary>
public sealed record CampaignSource(int PostId, long Revision, string Subject, string Slug, int? DocumentVersion,
    string? DocumentJson, string? DocumentPlainText, int? DocumentWordCount, int? DocumentReadingMinutes)
{
    public override string ToString() => nameof(CampaignSource);
}

public sealed record CampaignIntent(Guid Id, int PostId, long SourceRevision, NewsletterCampaignState State, long Version, string SnapshotHash);
public enum CampaignStartStatus { Created, Existing, Conflict, SourceChanged, NotFound, Invalid, NotConfigured }
public sealed record CampaignStartResult(CampaignStartStatus Status, CampaignIntent? Intent = null);
public enum CampaignCancelStatus { Cancelled, Unchanged, Conflict, NotFound, InvalidTime }

/// <summary>Atomic intent persistence only; no recipients, provider submission or public content projection.</summary>
public interface INewsletterCampaignStore
{
    Task<CampaignSource?> ReadSourceAsync(int postId, CancellationToken cancellationToken);
    Task<CampaignIntent?> FindIntentAsync(Guid startKey, CancellationToken cancellationToken);
    Task<CampaignStartResult> TryCreateAsync(NewsletterCampaign campaign, CancellationToken cancellationToken);
    Task<CampaignCancelStatus> TryCancelAsync(Guid id, long expectedVersion, DateTime utcNow, CancellationToken cancellationToken);
}
