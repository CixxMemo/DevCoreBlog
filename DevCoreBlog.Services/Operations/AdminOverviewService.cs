using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Models;
using Microsoft.Extensions.Logging;

namespace DevCoreBlog.Services.Operations;

/// <summary>Reads admin diagnostics and metrics under one deadline; unavailable metrics are never replaced with zeroes.</summary>
public sealed class AdminOverviewService(IDatabaseConnectionProbe database, IAdminDashboardService dashboard,
    MediaOperationStatus media, ILogger<AdminOverviewService> logger) : IAdminOverviewService
{
    public async Task<AdminOverviewSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await database.CheckAsync(deadline.Token);
            var metrics = await dashboard.GetSnapshotAsync(deadline.Token);
            return new(metrics, "Verified", media.GetSnapshot());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var state = deadline.IsCancellationRequested ? "Timed out" : "Unavailable";
            logger.LogWarning("Admin database read {DatabaseState}; ExceptionType: {ExceptionType}.", state, exception.GetType().Name);
            return new(null, state, media.GetSnapshot());
        }
    }
}

/// <summary>Exposes only safe operational summaries to the authorized MVC dashboard.</summary>
public interface IAdminOverviewService
{
    Task<AdminOverviewSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Keeps missing metrics distinct from an empty database.</summary>
public sealed record AdminOverviewSnapshot(AdminDashboardSnapshot? Dashboard, string DatabaseState, MediaStatusSnapshot Media);
