using DevCoreBlog.Core.Entities;
using DevCoreBlog.Core.Newsletters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Bounds durable snapshots and protects source history; the migration also installs immutability guards.</summary>
public sealed class NewsletterCampaignConfiguration : IEntityTypeConfiguration<NewsletterCampaign>
{
    public void Configure(EntityTypeBuilder<NewsletterCampaign> entity)
    {
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.StartKey).IsUnique();
        entity.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Restrict);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.Subject).HasMaxLength(300);
        entity.Property(x => x.SourceSlug).HasMaxLength(200);
        entity.Property(x => x.SenderEmail).HasMaxLength(254).UseCollation("C");
        entity.Property(x => x.SenderName).HasMaxLength(80);
        entity.Property(x => x.ReplyTo).HasMaxLength(254).UseCollation("C");
        entity.Property(x => x.ProviderContract).HasMaxLength(32).UseCollation("C");
        entity.Property(x => x.WebVersionUrl).HasMaxLength(2048);
        entity.Property(x => x.SnapshotHash).HasMaxLength(64).UseCollation("C");
        entity.ToTable("NewsletterCampaigns", table =>
        {
            table.HasCheckConstraint("CK_NewsletterCampaigns_Identity", """
                "Id" <> '00000000-0000-0000-0000-000000000000'::uuid
                AND "StartKey" <> '00000000-0000-0000-0000-000000000000'::uuid
                AND "SourceRevision" > 0 AND "SourceRevision" < 9223372036854775807
                AND "SnapshotHash" ~ '^[A-F0-9]{64}$'
                """);
            table.HasCheckConstraint("CK_NewsletterCampaigns_State", """
                (("State"=0 AND "Version"=1 AND "UpdatedUtc"="CreatedUtc") OR ("State"=1 AND "Version"=2))
                AND isfinite("CreatedUtc") AND isfinite("UpdatedUtc") AND "UpdatedUtc">="CreatedUtc"
                """);
            table.HasCheckConstraint("CK_NewsletterCampaigns_Content", """
                length(btrim("Subject")) BETWEEN 1 AND 300 AND "Subject" !~ '[[:cntrl:]]'
                AND length(btrim("SourceSlug")) BETWEEN 1 AND 200 AND "SourceSlug" !~ '[[:cntrl:]]'
                AND "DocumentVersion"=1 AND octet_length("DocumentJson") BETWEEN 1 AND 1048576
                AND octet_length("DocumentPlainText") <= 1048576 AND "DocumentWordCount" BETWEEN 0 AND 200000
                AND "DocumentReadingMinutes"=("DocumentWordCount"+199)/200 AND "RenderVersion"=1
                AND octet_length("Html") BETWEEN 1 AND 983040 AND octet_length("Text") BETWEEN 1 AND 245760
                AND "WebVersionUrl" ~ '^https://' AND "WebVersionUrl" !~ '[[:space:][:cntrl:]]'
                """);
            table.HasCheckConstraint("CK_NewsletterCampaigns_Sender", """
                length(btrim("SenderName")) BETWEEN 1 AND 80 AND "SenderName" !~ '[[:cntrl:]<>"\\]'
                AND length("SenderEmail") BETWEEN 3 AND 254 AND "SenderEmail" ~ '^[!-~]+@[!-~]+\.[!-~]+$'
                AND ("ReplyTo" IS NULL OR (length("ReplyTo") BETWEEN 3 AND 254 AND "ReplyTo" ~ '^[!-~]+@[!-~]+\.[!-~]+$'))
                AND "ProviderContract" ~ '^[A-Za-z0-9/.-]{1,32}$'
                """);
        });
    }
}
