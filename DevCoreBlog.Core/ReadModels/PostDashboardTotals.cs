namespace DevCoreBlog.Core.ReadModels;

/// <summary>Aggregated active-post counts and views for the admin dashboard.</summary>
public sealed record PostDashboardTotals(
    int TotalPosts,
    int PublishedPosts,
    int DraftPosts,
    long TotalViews);
