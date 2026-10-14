using DevCoreBlog.Core.Newsletters;

namespace DevCoreBlog.Models.Public;

/// <summary>Paging is presentation state; teaser items contain only the approved fields.</summary>
public sealed record NewsletterTeaserListModel(PublicNewsletterTeaserPage Result, int Page)
{
    public bool HasNext => Page < PublicNewsletterTeaserPage.MaximumPage &&
        (long)Page * PublicNewsletterTeaserPage.PageSize < Result.TotalCount;
}
