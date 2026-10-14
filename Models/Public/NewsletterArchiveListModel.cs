using DevCoreBlog.Core.Newsletters;

namespace DevCoreBlog.Models.Public;

/// <summary>Private paging is bounded independently of public teaser navigation.</summary>
public sealed record NewsletterArchiveListModel(NewsletterArchivePage Result, int Page)
{
    public bool HasNext => Page < NewsletterArchivePage.MaximumPage && Page * NewsletterArchivePage.PageSize < Result.TotalCount;
}
