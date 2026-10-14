using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Email;

/// <summary>Database bounds and restrictive foreign keys protect durable mail evidence.</summary>
public sealed class TransactionalEmailConfiguration : IEntityTypeConfiguration<TransactionalEmailJob>
{
    public void Configure(EntityTypeBuilder<TransactionalEmailJob> entity)
    {
        entity.ToTable("TransactionalEmailJobs", table =>
        {
            table.HasCheckConstraint("CK_TransactionalEmailJobs_Purpose", "\"Purpose\" IN (0,1)");
            table.HasCheckConstraint("CK_TransactionalEmailJobs_State", """
                "State" BETWEEN 0 AND 7 AND "Problem" BETWEEN 0 AND 9 AND "SubscriberVersion" > 0
                AND "AttemptCount" BETWEEN 0 AND 5
                AND (("AttemptCount" = 0) = ("FirstAttemptUtc" IS NULL))
                AND (("State" IN (1,2)) = ("LeaseId" IS NOT NULL AND "LeaseUntilUtc" IS NOT NULL))
                AND (("LeaseId" IS NULL) = ("LeaseUntilUtc" IS NULL))
                AND ("State" <> 2 OR "AttemptCount" > 0)
                AND (("State" = 3) = ("ProviderMessageId" IS NOT NULL))
                AND ("ProviderMessageId" IS NULL OR "ProviderMessageId" <> '00000000-0000-0000-0000-000000000000'::uuid)
                """);
            table.HasCheckConstraint("CK_TransactionalEmailJobs_Time", """
                isfinite("CreatedUtc") AND isfinite("UpdatedUtc") AND isfinite("ExpiresUtc") AND isfinite("DueUtc")
                AND "UpdatedUtc" >= "CreatedUtc" AND "ExpiresUtc" >= "CreatedUtc" + interval '30 seconds'
                AND "ExpiresUtc" <= "CreatedUtc" + interval '1 day' AND "DueUtc" >= "CreatedUtc"
                AND ("FirstAttemptUtc" IS NULL OR (isfinite("FirstAttemptUtc") AND "FirstAttemptUtc" BETWEEN "CreatedUtc" AND "UpdatedUtc"))
                AND ("LeaseUntilUtc" IS NULL OR isfinite("LeaseUntilUtc"))
                """);
            table.HasCheckConstraint("CK_TransactionalEmailJobs_Payload", """
                octet_length("ProtectedPayload") BETWEEN 1 AND 4194304
                AND "ProtectedPayload" ~ '^[A-Za-z0-9+/=]+$' AND "PayloadHash" ~ '^[A-F0-9]{64}$'
                """);
        });
        entity.Property(item => item.Purpose).HasDefaultValue(TransactionalEmailPurpose.Verification);
        entity.HasKey(item => item.Id);
        entity.Property(item => item.PayloadHash).HasMaxLength(64).UseCollation("C");
        entity.HasOne<Subscriber>().WithMany().HasForeignKey(item => item.SubscriberId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(item => new { item.DueUtc, item.Id }).HasFilter("\"State\" = 0");
        entity.HasIndex(item => item.LeaseUntilUtc).HasFilter("\"State\" IN (1,2)");
    }
}

/// <summary>Attempt outcomes are finite, bounded and distinct from confirmed delivery.</summary>
public sealed class TransactionalEmailAttemptConfiguration : IEntityTypeConfiguration<TransactionalEmailAttempt>
{
    public void Configure(EntityTypeBuilder<TransactionalEmailAttempt> entity)
    {
        entity.ToTable("TransactionalEmailAttempts", table => table.HasCheckConstraint("CK_TransactionalEmailAttempts_Valid", """
            "Number" BETWEEN 1 AND 5 AND isfinite("StartedUtc")
            AND (("CompletedUtc" IS NULL) = ("Outcome" IS NULL))
            AND (("Outcome" IS NULL) = ("Reason" IS NULL))
            AND ("Outcome" IS NULL OR "Outcome" BETWEEN 0 AND 4)
            AND ("Reason" IS NULL OR "Reason" BETWEEN 0 AND 15)
            AND ("CompletedUtc" IS NULL OR (isfinite("CompletedUtc") AND "CompletedUtc" >= "StartedUtc"))
            AND ("HttpStatus" IS NULL OR "HttpStatus" BETWEEN 100 AND 599)
            AND ("ProviderMessageId" IS NULL OR ("Outcome" = 0 AND "ProviderMessageId" <> '00000000-0000-0000-0000-000000000000'::uuid))
            """));
        entity.HasKey(item => new { item.JobId, item.Number });
        entity.HasOne<TransactionalEmailJob>().WithMany().HasForeignKey(item => item.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}
