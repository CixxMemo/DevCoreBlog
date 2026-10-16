using DevCoreBlog.Core.Newsletters;
namespace DevCoreBlog.Models.Admin;

/// <summary>Aggregate-only private view state with the observation time, not a worker heartbeat.</summary>
public sealed record CampaignMetricsModel(CampaignMetrics Metrics, DateTime ObservedUtc);
