using DevCoreBlog.Core.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Email;

/// <summary>Minimal intent and result stay immutable; body and credentials are absent from this history.</summary>
public sealed class TestEmailSubmissionConfiguration : IEntityTypeConfiguration<TestEmailSubmission>
{
    public void Configure(EntityTypeBuilder<TestEmailSubmission> e)
    {
        e.HasKey(x => x.Id); e.Property(x => x.InputHash).HasMaxLength(64).UseCollation("C");
        e.Property(x => x.AddressKey).HasMaxLength(254).UseCollation("C"); e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => x.CreatedUtc); e.HasIndex(x => x.ProviderMessageId).IsUnique().HasFilter("\"ProviderMessageId\" IS NOT NULL");
        e.ToTable("TestEmailSubmissions", t => t.HasCheckConstraint("CK_TestEmailSubmissions_Valid", """
            "Id"<>'00000000-0000-0000-0000-000000000000'::uuid AND "InputHash" ~ '^[A-F0-9]{64}$'
            AND length("AddressKey") BETWEEN 3 AND 254 AND "AddressKey"=lower("AddressKey") AND "AddressKey" ~ '^[!-~]+@[A-Za-z0-9.-]+$'
            AND isfinite("CreatedUtc") AND (("CompletedUtc" IS NULL)=("Outcome" IS NULL)) AND (("Outcome" IS NULL)=("Reason" IS NULL))
            AND (("Version"=1 AND "CompletedUtc" IS NULL) OR ("Version"=2 AND "CompletedUtc" IS NOT NULL))
            AND ("CompletedUtc" IS NULL OR (isfinite("CompletedUtc") AND "CompletedUtc">="CreatedUtc" AND "CompletedUtc"<"CreatedUtc"+interval '1 minute'))
            AND ("Outcome" IS NULL OR "Outcome" BETWEEN 0 AND 4) AND ("Reason" IS NULL OR "Reason" BETWEEN 0 AND 15)
            AND ("HttpStatus" IS NULL OR "HttpStatus" BETWEEN 100 AND 599)
            AND ("CompletedUtc" IS NOT NULL OR "HttpStatus" IS NULL)
            AND (("Outcome" IS NOT NULL AND "Outcome"=0 AND "ProviderMessageId" IS NOT NULL AND "ProviderMessageId"<>'00000000-0000-0000-0000-000000000000'::uuid)
                OR (("Outcome" IS NULL OR "Outcome"<>0) AND "ProviderMessageId" IS NULL))
            """));
    }
}
