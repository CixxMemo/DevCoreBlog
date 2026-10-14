using DevCoreBlog.Core.Documents;
using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Services.Documents;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Trusted configuration only. Missing sender/HTTPS origin disables new campaign preparation without breaking the site.</summary>
public sealed class NewsletterCampaignOrigin
{
    public NewsletterCampaignOrigin(string trustedOrigin)
    {
        if (Uri.TryCreate(trustedOrigin, UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0
            && url.AbsolutePath == "/" && url.Query.Length == 0 && url.Fragment.Length == 0)
            Origin = url.GetLeftPart(UriPartial.Authority);
    }
    public string? Origin { get; }
    public string WebVersion(string slug) => Origin is { } origin
        ? origin + "/post/" + Uri.EscapeDataString(slug) : throw new InvalidOperationException("Campaign origin unavailable.");
}

/// <summary>Trusted internal preparation; the future authorized start UI is separate. No sender/queue/recipient dependency exists.</summary>
public sealed class NewsletterCampaignService(INewsletterCampaignStore store, IEmailSenderProfile sender,
    NewsletterCampaignOrigin origin, ContentDocumentValidator validator, DocumentTextProducer text,
    DocumentEmailRenderer renderer, TimeProvider clock)
{
    public async Task<CampaignStartResult> PrepareAsync(int postId, long expectedRevision, Guid startKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (postId <= 0 || expectedRevision is <= 0 or long.MaxValue || startKey == Guid.Empty)
            return new(CampaignStartStatus.Invalid);
        // Replaying the same intent returns its original snapshot even after a source edit or config rotation.
        var prior = await store.FindIntentAsync(startKey, cancellationToken);
        if (prior is not null) return new(prior.PostId == postId && prior.SourceRevision == expectedRevision
            ? CampaignStartStatus.Existing : CampaignStartStatus.Conflict, prior);
        if (sender.Identity is not { } identity || origin.Origin is null) return new(CampaignStartStatus.NotConfigured);
        var source = await store.ReadSourceAsync(postId, cancellationToken);
        if (source is null) return new(CampaignStartStatus.NotFound);
        if (source.Revision != expectedRevision) return new(CampaignStartStatus.SourceChanged);
        var canonical = StoredDocumentReader.Read(new(source.Revision, source.DocumentVersion, source.DocumentJson,
            source.DocumentPlainText, source.DocumentWordCount, source.DocumentReadingMinutes), validator, text);
        if (canonical.Status != PostDocumentReadStatus.Ready || canonical.Document is null) return new(CampaignStartStatus.Invalid);
        NewsletterCampaign campaign;
        try
        {
            var web = origin.WebVersion(source.Slug);
            var body = renderer.Render(canonical.Document, EmailRenderContext.Create(web));
            campaign = NewsletterCampaign.Prepare(startKey, source, identity, sender.Contract, web, body.Html, body.PlainText, clock.GetUtcNow().UtcDateTime);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return new(CampaignStartStatus.Invalid); }
        return await store.TryCreateAsync(campaign, cancellationToken);
    }

    public Task<CampaignCancelStatus> CancelAsync(Guid id, long expectedVersion, CancellationToken cancellationToken) =>
        store.TryCancelAsync(id, expectedVersion, clock.GetUtcNow().UtcDateTime, cancellationToken);
}
