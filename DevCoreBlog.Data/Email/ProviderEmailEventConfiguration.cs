using DevCoreBlog.Core.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Email;

/// <summary>Provider IDs and normalized addresses remain distinct from campaign intent and provider acceptance.</summary>
public sealed class ProviderEmailEventConfiguration : IEntityTypeConfiguration<ProviderEmailEvent>
{
    public void Configure(EntityTypeBuilder<ProviderEmailEvent> e)
    {
        e.HasKey(x => x.EventId);
        e.Property(x => x.EventId).HasMaxLength(128).UseCollation("C");
        e.Property(x => x.PayloadHash).HasMaxLength(64).UseCollation("C");
        e.Property(x => x.AddressKey).HasMaxLength(254).UseCollation("C");
        e.HasIndex(x => new { x.MessageId, x.AddressKey });
        e.ToTable("ProviderEmailEvents", t => t.HasCheckConstraint("CK_ProviderEmailEvents_Valid", """
            "EventId" ~ '^[A-Za-z0-9_-]{1,128}$' AND "PayloadHash" ~ '^[A-Fa-f0-9]{64}$'
            AND "MessageId"<>'00000000-0000-0000-0000-000000000000'::uuid AND "Kind" IN (0,1,2,3)
            AND octet_length("AddressKey") BETWEEN 3 AND 254 AND "AddressKey"=lower("AddressKey" COLLATE "C")
            AND isfinite("OccurredUtc") AND isfinite("ReceivedUtc") AND "OccurredUtc"<="ReceivedUtc"+interval '5 minutes'
            """));
    }
}

/// <summary>Each observation is a distinct message/address fact; first event times can only become earlier.</summary>
public sealed class ProviderEmailObservationConfiguration : IEntityTypeConfiguration<ProviderEmailObservation>
{
    public void Configure(EntityTypeBuilder<ProviderEmailObservation> e)
    {
        e.HasKey(x => new { x.MessageId, x.AddressKey });
        e.Property(x => x.AddressKey).HasMaxLength(254).UseCollation("C");
        e.ToTable("ProviderEmailObservations", t => t.HasCheckConstraint("CK_ProviderEmailObservations_Valid", """
            "MessageId"<>'00000000-0000-0000-0000-000000000000'::uuid
            AND octet_length("AddressKey") BETWEEN 3 AND 254 AND "AddressKey"=lower("AddressKey" COLLATE "C")
            AND ("DeliveredUtc" IS NOT NULL OR "BouncedUtc" IS NOT NULL OR "ComplainedUtc" IS NOT NULL OR "TemporaryBounceUtc" IS NOT NULL)
            AND ("DeliveredUtc" IS NULL OR isfinite("DeliveredUtc")) AND ("BouncedUtc" IS NULL OR isfinite("BouncedUtc"))
            AND ("ComplainedUtc" IS NULL OR isfinite("ComplainedUtc")) AND ("TemporaryBounceUtc" IS NULL OR isfinite("TemporaryBounceUtc"))
            """));
    }
}

/// <summary>Irreversible bounce/complaint flags, including addresses without a local subscriber identity.</summary>
public sealed class ProviderAddressSuppressionConfiguration : IEntityTypeConfiguration<ProviderAddressSuppression>
{
    public void Configure(EntityTypeBuilder<ProviderAddressSuppression> e)
    {
        e.HasKey(x => x.AddressKey);
        e.Property(x => x.AddressKey).HasMaxLength(254).UseCollation("C");
        e.ToTable("ProviderAddressSuppressions", t => t.HasCheckConstraint("CK_ProviderAddressSuppressions_Valid", """
            octet_length("AddressKey") BETWEEN 3 AND 254 AND "AddressKey"=lower("AddressKey" COLLATE "C")
            AND "Flags" IN (1,2,3) AND isfinite("UpdatedUtc")
            """));
    }
}
