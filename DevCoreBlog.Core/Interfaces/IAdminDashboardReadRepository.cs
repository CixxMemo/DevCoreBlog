using DevCoreBlog.Core.ReadModels;

namespace DevCoreBlog.Core.Interfaces;

/// <summary>Exposes only the bounded database reads needed by the admin dashboard.</summary>
public interface IAdminDashboardReadRepository
{
    Task<PostDashboardTotals> GetPostTotalsAsync(CancellationToken cancellationToken = default);
    Task<int> GetActiveCategoryCountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopReadPost>> GetTopReadPostsAsync(
        CancellationToken cancellationToken = default);
}
