using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Publishing;

namespace DevCoreBlog.Services.Publishing;

public sealed record PublicationSettingsPage(PublicationSettings? Settings, EmailSenderIdentity? ConfiguredSender)
{
    public bool SenderApproved => Settings?.Approves(ConfiguredSender) == true;
    public override string ToString() => nameof(PublicationSettingsPage);
}

/// <summary>Editable editorial text plus explicit approval of the existing immutable transport identity; no DNS or send side effects.</summary>
public sealed class PublicationSettingsService(IPublicationSettingsStore store, IEmailSenderProfile sender, TimeProvider clock)
{
    public async Task<PublicationSettingsPage> ReadAsync(CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        return new(await store.ReadAsync(deadline.Token), sender.Identity);
    }
    public async Task<PublicationSaveStatus> SaveAsync(string name, string cadence, long version, bool approve,
        string? postedEmail, string? postedName, CancellationToken ct)
    {
        var identity = sender.Identity;
        if (postedEmail != identity?.Email || postedName != identity?.Name || (approve && identity is null))
            return PublicationSaveStatus.SenderMismatch;
        if (version is < 0 or >= long.MaxValue - 1) return PublicationSaveStatus.Invalid;
        PublicationSettings proposed;
        try { proposed = PublicationSettings.Create(name, cadence, approve ? identity : null, version + 1, clock.GetUtcNow().UtcDateTime); }
        catch (ArgumentException) { return PublicationSaveStatus.Invalid; }
        using var deadline = Deadline(ct);
        return await store.SaveAsync(proposed, version, deadline.Token);
    }
    private static CancellationTokenSource Deadline(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct); source.CancelAfter(TimeSpan.FromSeconds(10)); return source;
    }
}
