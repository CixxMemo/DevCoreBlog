using DevCoreBlog.Core.ReadModels;

namespace DevCoreBlog.Services.Models;

/// <summary>Contains only the aggregates and ranking rendered by the dashboard.</summary>
public sealed record AdminDashboardSnapshot(
    int TotalPosts,
    int PublishedPosts,
    int DraftPosts,
    long TotalViews,
    int CategoryCount,
    IReadOnlyList<TopReadPost> TopPosts);
