using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Models;

namespace DevCoreBlog.Services;

/// <summary>Builds one dashboard snapshot from sequential bounded repository reads.</summary>
public sealed class AdminDashboardService : IAdminDashboardService
{
    private readonly IAdminDashboardReadRepository _dashboardReads;

    public AdminDashboardService(IAdminDashboardReadRepository dashboardReads)
    {
        _dashboardReads = dashboardReads;
    }

    public async Task<AdminDashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        // Scoped repositories share a DbContext, so these reads must remain sequential.
        var totals = await _dashboardReads.GetPostTotalsAsync(cancellationToken);
        var categoryCount = await _dashboardReads.GetActiveCategoryCountAsync(cancellationToken);
        var topPosts = await _dashboardReads.GetTopReadPostsAsync(cancellationToken);
        return new AdminDashboardSnapshot(
            totals.TotalPosts,
            totals.PublishedPosts,
            totals.DraftPosts,
            totals.TotalViews,
            categoryCount,
            topPosts);
    }
}
