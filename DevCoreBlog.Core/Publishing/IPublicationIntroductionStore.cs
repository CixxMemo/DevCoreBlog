namespace DevCoreBlog.Core.Publishing;

/// <summary>Public editorial copy only; sender approval and transport details never cross this boundary.</summary>
public sealed record PublicationIntroduction(string Name, string Cadence);

public interface IPublicationIntroductionStore
{
    Task<PublicationIntroduction?> ReadAsync(CancellationToken cancellationToken);
}
