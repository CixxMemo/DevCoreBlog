using System.Text;
using DevCoreBlog.Core.Newsletters;

namespace DevCoreBlog.Services.Newsletters;

/// <summary>Bounds public summary text without consulting private body, excerpt or media fallbacks.</summary>
public sealed class PublicNewsletterTeaserService(IPublicNewsletterTeasers teasers, TimeProvider clock)
{
    public async Task<PublicNewsletterTeaserPage> ReadPageAsync(int page, CancellationToken cancellationToken)
    {
        if (page is < 1 or > PublicNewsletterTeaserPage.MaximumPage) throw new ArgumentOutOfRangeException(nameof(page));
        var result = await teasers.ReadPageAsync(page, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(result.Items.Select(item => new PublicNewsletterTeaser(item.Title,
            string.Concat(item.Summary.EnumerateRunes().Take(PublicNewsletterTeaserPage.SummaryRunes)
                .Select(rune => rune.ToString())))).ToArray(), result.TotalCount);
    }
}
