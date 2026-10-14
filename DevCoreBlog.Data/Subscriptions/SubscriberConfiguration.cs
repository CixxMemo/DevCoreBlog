using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Database invariants reject invalid state even when EF validation is bypassed.</summary>
public sealed class SubscriberConfiguration : IEntityTypeConfiguration<Subscriber>
{
    public void Configure(EntityTypeBuilder<Subscriber> entity)
    {
        entity.HasKey(item => item.Id);
        entity.Property(item => item.EmailAddress).HasMaxLength(254).UseCollation("C");
        entity.Property(item => item.AddressKey).HasMaxLength(254).UseCollation("C");
        entity.HasIndex(item => item.AddressKey).IsUnique();
        entity.Property(item => item.Version).IsConcurrencyToken();
        entity.Property(item => item.PeriodId).HasDefaultValueSql("gen_random_uuid()");
        entity.Ignore(item => item.IsSendEligible);
        entity.ToTable("Subscribers", table =>
        {
            table.HasCheckConstraint("CK_Subscribers_Period", "\"PeriodId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Subscribers_Address", """
                octet_length("EmailAddress") BETWEEN 3 AND 254
                AND "EmailAddress" ~ '^[A-Za-z0-9!#$%&''*+/=?^_`{|}~.-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$'
                AND length(split_part("EmailAddress", '@', 1)) BETWEEN 1 AND 64
                AND split_part("EmailAddress", '@', 1) NOT LIKE '.%'
                AND split_part("EmailAddress", '@', 1) NOT LIKE '%.'
                AND position('..' in split_part("EmailAddress", '@', 1)) = 0
                AND split_part("EmailAddress", '@', 2) !~ '(^|\.)-|-(\.|$)|[A-Za-z0-9-]{64}'
                AND "AddressKey" = lower("EmailAddress" COLLATE "C")
                """);
            table.HasCheckConstraint("CK_Subscribers_State", """
                "Status" IN (0,1,2) AND "Suppression" BETWEEN 0 AND 7 AND "Version" > 0
                AND ("Status" <> 0 OR "VerifiedUtc" IS NULL)
                AND ("Status" <> 1 OR "VerifiedUtc" IS NOT NULL)
                AND ("Status" <> 2 OR "UnsubscribedUtc" IS NOT NULL)
                AND ("Status" <> 1 OR "UnsubscribedUtc" IS NULL OR "VerifiedUtc" >= "UnsubscribedUtc")
                """);
            table.HasCheckConstraint("CK_Subscribers_Time", """
                isfinite("CreatedUtc") AND isfinite("UpdatedUtc") AND "UpdatedUtc" >= "CreatedUtc"
                AND ("VerifiedUtc" IS NULL OR (isfinite("VerifiedUtc") AND "VerifiedUtc" BETWEEN "CreatedUtc" AND "UpdatedUtc"))
                AND ("UnsubscribedUtc" IS NULL OR (isfinite("UnsubscribedUtc") AND "UnsubscribedUtc" BETWEEN "CreatedUtc" AND "UpdatedUtc"))
                """);
        });
    }
}
