using DevCoreBlog.Services.Models;

namespace DevCoreBlog.Services.Interfaces;

/// <summary>Provides the bounded admin dashboard read model.</summary>
public interface IAdminDashboardService
{
    Task<AdminDashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}
