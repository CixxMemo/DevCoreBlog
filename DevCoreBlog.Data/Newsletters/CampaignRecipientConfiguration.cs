using DevCoreBlog.Core.Newsletters;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Frozen selection bounds and monotonic progress are also guarded by migration triggers.</summary>
public sealed class CampaignRecipientSelectionConfiguration : IEntityTypeConfiguration<NewsletterRecipientSelection>
{
    public void Configure(EntityTypeBuilder<NewsletterRecipientSelection> e)
    {
        e.HasKey(x => x.CampaignId);
        e.HasOne<NewsletterCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.ToTable("NewsletterRecipientSelections", t => t.HasCheckConstraint("CK_RecipientSelections_Progress", """
            "CampaignId" <> '00000000-0000-0000-0000-000000000000'::uuid AND "Version">0 AND "State" IN (0,1)
            AND isfinite("CutoffUtc") AND isfinite("UpdatedUtc") AND "UpdatedUtc">="CutoffUtc"
            AND "ScannedCount">=0 AND "SelectedCount" BETWEEN 0 AND "ScannedCount"
            AND ("UpperSubscriberId" IS NULL OR "UpperSubscriberId"<>'00000000-0000-0000-0000-000000000000'::uuid)
            AND (("LastSubscriberId" IS NULL AND "ScannedCount"=0) OR
                ("LastSubscriberId" IS NOT NULL AND "ScannedCount">0 AND "UpperSubscriberId" IS NOT NULL
                 AND "LastSubscriberId">'00000000-0000-0000-0000-000000000000'::uuid AND "LastSubscriberId"<="UpperSubscriberId"))
            """));
    }
}

/// <summary>One immutable lifecycle-bound address per campaign/subscriber; deletion never cascades into history.</summary>
public sealed class CampaignRecipientConfiguration : IEntityTypeConfiguration<NewsletterRecipient>
{
    public void Configure(EntityTypeBuilder<NewsletterRecipient> e)
    {
        e.HasKey(x => new { x.CampaignId, x.SubscriberId });
        e.HasIndex(x => new { x.CampaignId, x.AddressKey }).IsUnique();
        e.HasIndex(x => new { x.CampaignId, x.SelectionVersion });
        e.HasOne<NewsletterRecipientSelection>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.EmailAddress).HasMaxLength(254).UseCollation("C");
        e.Property(x => x.AddressKey).HasMaxLength(254).UseCollation("C");
        e.ToTable("NewsletterRecipients", t => t.HasCheckConstraint("CK_NewsletterRecipients_Binding", """
            "SubscriberId"<>'00000000-0000-0000-0000-000000000000'::uuid AND "PeriodId"<>'00000000-0000-0000-0000-000000000000'::uuid
            AND "SubscriberVersion">0 AND "SelectionVersion">=2 AND isfinite("SelectedUtc")
            AND octet_length("EmailAddress") BETWEEN 3 AND 254 AND "AddressKey"=lower("EmailAddress" COLLATE "C")
            """));
    }
}
