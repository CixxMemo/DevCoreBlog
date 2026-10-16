using DevCoreBlog.Core.Newsletters;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Caps the complete observation; storage failure remains unknown rather than fabricated zero metrics.</summary>
public sealed class CampaignMetricsService(ICampaignMetricsStore store)
{
    public async Task<CampaignMetrics?> ReadAsync(Guid id, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return await store.ReadAsync(id, deadline.Token);
    }
}
