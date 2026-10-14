using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>Proof/session bounds and restrictive history keys also hold for writes outside EF.</summary>
public sealed class ReaderLoginTokenConfiguration : IEntityTypeConfiguration<ReaderLoginToken>
{
    public void Configure(EntityTypeBuilder<ReaderLoginToken> e)
    {
        e.ToTable("ReaderLoginTokens", t => t.HasCheckConstraint("CK_ReaderLoginTokens_Valid", """
            "Id" <> '00000000-0000-0000-0000-000000000000'::uuid AND "SubscriberVersion" > 0
            AND "PeriodId" <> '00000000-0000-0000-0000-000000000000'::uuid
            AND "Digest" ~ '^[A-F0-9]{64}$' AND "AddressBinding" ~ '^[A-F0-9]{64}$'
            AND isfinite("CreatedUtc") AND isfinite("ExpiresUtc") AND "ExpiresUtc" = "CreatedUtc" + interval '15 minutes'
            AND ("ConsumedUtc" IS NULL OR (isfinite("ConsumedUtc") AND "ConsumedUtc" >= "CreatedUtc" AND "ConsumedUtc" < "ExpiresUtc"))
            """));
        e.HasKey(x => x.Id); e.Property(x => x.Digest).HasMaxLength(64); e.Property(x => x.AddressBinding).HasMaxLength(64);
        e.HasIndex(x => x.Digest).IsUnique(); e.HasIndex(x => new { x.SubscriberId, x.CreatedUtc });
        e.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<DevCoreBlog.Core.Email.TransactionalEmailJob>().WithMany().HasForeignKey(x => x.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ReaderSessionConfiguration : IEntityTypeConfiguration<ReaderSession>
{
    public void Configure(EntityTypeBuilder<ReaderSession> e)
    {
        e.ToTable("ReaderSessions", t => t.HasCheckConstraint("CK_ReaderSessions_Valid", """
            "Id" <> '00000000-0000-0000-0000-000000000000'::uuid AND "SubscriberVersion" > 0
            AND "PeriodId" <> '00000000-0000-0000-0000-000000000000'::uuid
            AND isfinite("CreatedUtc") AND isfinite("ExpiresUtc") AND "ExpiresUtc" = "CreatedUtc" + interval '7 days'
            AND ("RevokedUtc" IS NULL OR (isfinite("RevokedUtc") AND "RevokedUtc" >= "CreatedUtc"))
            """));
        e.HasKey(x => x.Id); e.HasIndex(x => x.SubscriberId);
        e.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ReaderLoginBudgetConfiguration : IEntityTypeConfiguration<ReaderLoginBudget>
{
    public void Configure(EntityTypeBuilder<ReaderLoginBudget> e)
    {
        e.ToTable("ReaderLoginBudgets", t => t.HasCheckConstraint("CK_ReaderLoginBudgets_Valid", """
            "Id" = 1 AND "Count" BETWEEN 0 AND 20 AND isfinite("WindowStartUtc") AND isfinite("UpdatedUtc")
            AND "WindowStartUtc" = date_trunc('day', "WindowStartUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AND "UpdatedUtc" >= "WindowStartUtc" AND "UpdatedUtc" < "WindowStartUtc" + interval '1 day'
            """));
        e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
    }
}
