namespace DevCoreBlog.Core.Interfaces;

/// <summary>Checks persistence connectivity without exposing provider details.</summary>
public interface IDatabaseConnectionProbe
{
    Task CheckAsync(CancellationToken cancellationToken);
}
