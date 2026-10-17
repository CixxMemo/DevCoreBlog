using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Purpose, digest, exact lifetime and restrictive links are enforced even outside EF.</summary>
public sealed class SubscriberVerificationConfiguration : IEntityTypeConfiguration<SubscriberVerificationToken>
{
    public void Configure(EntityTypeBuilder<SubscriberVerificationToken> entity)
    {
        entity.ToTable("SubscriberVerificationTokens", table => table.HasCheckConstraint("CK_SubscriberVerificationTokens_Valid", """
            "Purpose" = 1 AND "SubscriberVersion" > 0
            AND (("RedactedUtc" IS NULL AND "Digest" IS NOT NULL AND "Digest" ~ '^[A-F0-9]{64}$')
                OR ("RedactedUtc" IS NOT NULL AND isfinite("RedactedUtc") AND "Digest" IS NULL
                    AND "RedactedUtc">greatest("ExpiresUtc","ConsumedUtc") + interval '604800 seconds'))
            AND "AddressBinding" ~ '^[A-F0-9]{64}$'
            AND isfinite("CreatedUtc") AND isfinite("ExpiresUtc") AND "ExpiresUtc" = "CreatedUtc" + interval '86400 seconds'
            AND ("ConsumedUtc" IS NULL OR (isfinite("ConsumedUtc") AND "ConsumedUtc" >= "CreatedUtc" AND "ConsumedUtc" < "ExpiresUtc"))
            """));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Digest).HasMaxLength(64).UseCollation("C");
        entity.Property(x => x.AddressBinding).HasMaxLength(64).UseCollation("C");
        entity.HasIndex(x => new { x.SubscriberId, x.CreatedUtc });
        entity.HasIndex(x => x.Digest).IsUnique();
        entity.HasIndex(x => new { x.SubscriberId, x.SubscriberVersion, x.ExpiresUtc }).HasFilter("\"ConsumedUtc\" IS NULL");
        entity.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<TransactionalEmailJob>().WithMany().HasForeignKey(x => x.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
