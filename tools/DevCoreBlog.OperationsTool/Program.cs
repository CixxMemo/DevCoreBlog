using DevCoreBlog.Core.Interfaces;
using DevCoreBlog.Services.Interfaces;
using DevCoreBlog.Services.Models;
using DevCoreBlog.Services.Operations;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

var checks = new Dictionary<string, bool>();
var log = new CaptureLog<AdminOverviewService>();
var media = new MediaOperationStatus(true, TimeProvider.System);
checks["configured_does_not_claim_provider_access"] = media.GetSnapshot().LastSuccess is null;
media.Record(true); var success = media.GetSnapshot().LastSuccess;
media.Record(false);
checks["failed_operation_preserves_historical_success_without_claiming_current_health"] = media.GetSnapshot().LastSucceeded == false && media.GetSnapshot().LastSuccess == success;
checks["process_restart_discards_old_observation"] = new MediaOperationStatus(true, TimeProvider.System).GetSnapshot().LastCompleted is null;
checks["missing_credentials_remain_not_configured"] = !new MediaOperationStatus(false, TimeProvider.System).GetSnapshot().Configured;
var metrics = new Metrics();
var clock = Stopwatch.StartNew();
var timeout = await new AdminOverviewService(new Probe(true), metrics, media, log).GetAsync(default);
checks["deadline_cancels_probe_and_never_queries_metrics"] = timeout.DatabaseState == "Timed out" && timeout.Dashboard is null && metrics.Calls == 0 && clock.Elapsed < TimeSpan.FromSeconds(4);
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await new AdminOverviewService(new Probe(true), metrics, media, log).GetAsync(cancelled.Token); checks["client_cancellation_is_propagated"] = false; }
catch (OperationCanceledException) { checks["client_cancellation_is_propagated"] = true; }
var failed = await new AdminOverviewService(new Probe(false), metrics, media, log).GetAsync(default);
checks["provider_error_has_no_fabricated_metrics"] = failed.Dashboard is null && failed.DatabaseState == "Unavailable";
checks["provider_exception_message_is_not_logged"] = log.Messages.All(m => !m.Contains("PRIVATE_CANARY"));
checks["no_error_level_for_deadline_or_client_cancellation"] = log.Levels.All(level => level != LogLevel.Error);
Console.WriteLine(JsonSerializer.Serialize(new { checks, count = checks.Count }, new JsonSerializerOptions { WriteIndented = true }));
return checks.Values.All(value => value) ? 0 : 1;

sealed class Probe(bool waits) : IDatabaseConnectionProbe
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (waits) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        else throw new InvalidOperationException("PRIVATE_CANARY connection credential or SQL");
    }
}
sealed class Metrics : IAdminDashboardService
{
    public int Calls { get; private set; }
    public Task<AdminDashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    { Calls++; throw new InvalidOperationException("Metrics must not be read after an unsuccessful probe."); }
}
sealed class CaptureLog<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public List<LogLevel> Levels { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    { Levels.Add(level); Messages.Add(formatter(state, exception)); }
}
