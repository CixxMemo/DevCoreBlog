using DevCoreBlog.Core.Newsletters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Newsletters;

/// <summary>Explicit immutable send intent and bounded durable preparation progress.</summary>
public sealed class CampaignDispatchConfiguration:IEntityTypeConfiguration<CampaignDispatch>
{
    public void Configure(EntityTypeBuilder<CampaignDispatch> e)
    {
        e.HasKey(x=>x.CampaignId);e.HasOne<NewsletterCampaign>().WithMany().HasForeignKey(x=>x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x=>x.ContextHash).HasMaxLength(64).UseCollation("C");e.Property(x=>x.SnapshotHash).HasMaxLength(64).UseCollation("C");
        e.Property(x=>x.Version).IsConcurrencyToken();e.HasIndex(x=>new{x.CreatedUtc,x.CampaignId}).HasFilter("\"PreparationState\"=0");
        e.ToTable("CampaignDispatches",t=>t.HasCheckConstraint("CK_CampaignDispatches_Valid","""
            "Version">0 AND "StagedCount">=0 AND "PreparationState" IN (0,1,2)
            AND "ContextHash" ~ '^[A-F0-9]{64}$' AND "SnapshotHash" ~ '^[A-F0-9]{64}$'
            AND octet_length("ProtectedContext") BETWEEN 1 AND 16384 AND "ProtectedContext" ~ '^[A-Za-z0-9+/=]+$'
            AND isfinite("CreatedUtc") AND isfinite("UpdatedUtc") AND "UpdatedUtc">="CreatedUtc"
            AND "ExpiresUtc"="CreatedUtc"+interval '1 day' AND isfinite("ExpiresUtc")
            AND (("StagedCount"=0 AND "LastSubscriberId" IS NULL) OR ("StagedCount">0 AND "LastSubscriberId" IS NOT NULL AND "LastSubscriberId">'00000000-0000-0000-0000-000000000000'::uuid))
            """));
    }
}

/// <summary>Selected membership, encrypted payload and restrictive history links cannot be rebound by a lifecycle update.</summary>
public sealed class CampaignDeliveryJobConfiguration:IEntityTypeConfiguration<CampaignDeliveryJob>
{
    public void Configure(EntityTypeBuilder<CampaignDeliveryJob> e)
    {
        e.HasKey(x=>x.Id);e.Property(x=>x.Version).IsConcurrencyToken();e.Property(x=>x.PayloadHash).HasMaxLength(64).UseCollation("C");
        e.HasOne<CampaignDispatch>().WithMany().HasForeignKey(x=>x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<NewsletterRecipient>().WithMany().HasForeignKey(x=>new{x.CampaignId,x.SubscriberId}).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x=>new{x.CampaignId,x.SubscriberId}).IsUnique();e.HasIndex(x=>new{x.CampaignId,x.PreparationVersion});
        e.HasIndex(x=>new{x.DueUtc,x.Id}).HasFilter("\"State\"=0");e.HasIndex(x=>x.LeaseUntilUtc).HasFilter("\"State\" IN (1,2)");
        e.HasIndex(x=>x.ProviderMessageId).IsUnique().HasFilter("\"ProviderMessageId\" IS NOT NULL");
        e.ToTable("CampaignDeliveryJobs",t=>
        {
            t.HasCheckConstraint("CK_CampaignDeliveryJobs_State","""
                "Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "PeriodId"<>'00000000-0000-0000-0000-000000000000'::uuid
                AND "SubscriberVersion">0 AND "PreparationVersion">=2 AND "Version">0 AND "State" BETWEEN 0 AND 7 AND "Problem" BETWEEN 0 AND 10
                AND "AttemptCount" BETWEEN 0 AND 5 AND (("AttemptCount"=0)=("FirstAttemptUtc" IS NULL))
                AND (("State" IN (1,2))=("LeaseId" IS NOT NULL AND "LeaseUntilUtc" IS NOT NULL))
                AND (("LeaseId" IS NULL)=("LeaseUntilUtc" IS NULL)) AND ("State"<>2 OR "AttemptCount">0)
                AND (("State"=3)=("ProviderMessageId" IS NOT NULL))
                AND ("ProviderMessageId" IS NULL OR "ProviderMessageId"<>'00000000-0000-0000-0000-000000000000'::uuid)
                """);
            t.HasCheckConstraint("CK_CampaignDeliveryJobs_Time","""
                isfinite("CreatedUtc") AND isfinite("UpdatedUtc") AND isfinite("ExpiresUtc") AND isfinite("DueUtc")
                AND "UpdatedUtc">="CreatedUtc" AND "ExpiresUtc">="CreatedUtc"+interval '30 seconds'
                AND "ExpiresUtc"<="CreatedUtc"+interval '1 day' AND "DueUtc">="CreatedUtc"
                AND ("FirstAttemptUtc" IS NULL OR (isfinite("FirstAttemptUtc") AND "FirstAttemptUtc" BETWEEN "CreatedUtc" AND "UpdatedUtc"))
                AND ("LeaseUntilUtc" IS NULL OR (isfinite("LeaseUntilUtc") AND "LeaseUntilUtc">"UpdatedUtc"))
                """);
            t.HasCheckConstraint("CK_CampaignDeliveryJobs_Payload","""
                octet_length("ProtectedPayload") BETWEEN 1 AND 4194304 AND "ProtectedPayload" ~ '^[A-Za-z0-9+/=]+$' AND "PayloadHash" ~ '^[A-F0-9]{64}$'
                """);
        });
    }
}

/// <summary>One irreversible outcome per durable start, fenced to its lease token.</summary>
public sealed class CampaignDeliveryAttemptConfiguration:IEntityTypeConfiguration<CampaignDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<CampaignDeliveryAttempt> e)
    {
        e.HasKey(x=>new{x.JobId,x.Number});e.HasOne<CampaignDeliveryJob>().WithMany().HasForeignKey(x=>x.JobId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable("CampaignDeliveryAttempts",t=>t.HasCheckConstraint("CK_CampaignDeliveryAttempts_Valid","""
            "Number" BETWEEN 1 AND 5 AND "LeaseId"<>'00000000-0000-0000-0000-000000000000'::uuid AND isfinite("StartedUtc")
            AND (("CompletedUtc" IS NULL)=("Outcome" IS NULL)) AND (("Outcome" IS NULL)=("Reason" IS NULL))
            AND ("Outcome" IS NULL OR "Outcome" BETWEEN 0 AND 4) AND ("Reason" IS NULL OR "Reason" BETWEEN 0 AND 15)
            AND ("CompletedUtc" IS NULL OR (isfinite("CompletedUtc") AND "CompletedUtc">="StartedUtc"))
            AND ("HttpStatus" IS NULL OR "HttpStatus" BETWEEN 100 AND 599)
            AND ("ProviderMessageId" IS NULL OR ("Outcome"=0 AND "ProviderMessageId"<>'00000000-0000-0000-0000-000000000000'::uuid))
            """));
    }
}
