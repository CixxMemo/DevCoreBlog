namespace DevCoreBlog.Services.Operations;

/// <summary>Stores only completed upload observations for this process, never credentials or media identifiers.</summary>
public sealed class MediaOperationStatus(bool configured, TimeProvider clock)
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastSuccess;
    private bool? _lastSucceeded;
    private DateTimeOffset? _lastCompleted;

    public void Record(bool succeeded)
    {
        lock (_gate)
        {
            _lastCompleted = clock.GetUtcNow();
            _lastSucceeded = succeeded;
            if (succeeded) _lastSuccess = _lastCompleted;
        }
    }

    public MediaStatusSnapshot GetSnapshot()
    {
        lock (_gate) return new(configured, _lastSucceeded, _lastCompleted, _lastSuccess);
    }
}

/// <summary>Distinguishes configured credentials from historical verified operation results.</summary>
public sealed record MediaStatusSnapshot(bool Configured, bool? LastSucceeded,
    DateTimeOffset? LastCompleted, DateTimeOffset? LastSuccess);
