using DevCoreBlog.Core.Email;
using DevCoreBlog.Core.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Retention;

/// <summary>Receipt retention and a monotonic checkpoint are independent of worker availability.</summary>
public sealed class RetentionConfiguration : IEntityTypeConfiguration<RetentionCheckpoint>
{
    public void Configure(EntityTypeBuilder<RetentionCheckpoint> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.ToTable("RetentionCheckpoints", t => t.HasCheckConstraint("CK_RetentionCheckpoint", "\"Id\"=1 AND isfinite(\"LastAppliedUtc\")"));
    }
}

public sealed class ProviderEmailReceiptConfiguration : IEntityTypeConfiguration<ProviderEmailReceipt>
{
    public void Configure(EntityTypeBuilder<ProviderEmailReceipt> e)
    {
        e.HasKey(x => x.EventId);
        e.Property(x => x.EventId).HasMaxLength(128).UseCollation("C");
        e.Property(x => x.PayloadHash).HasMaxLength(64).UseCollation("C");
        e.ToTable("ProviderEmailReceipts", t => t.HasCheckConstraint("CK_ProviderEmailReceipt",
            "\"EventId\" ~ '^[A-Za-z0-9_-]{1,128}$' AND \"PayloadHash\" ~ '^[A-Fa-f0-9]{64}$' AND isfinite(\"ReceivedUtc\")"));
    }
}
