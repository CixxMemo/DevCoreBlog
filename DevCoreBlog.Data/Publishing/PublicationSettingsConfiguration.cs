using DevCoreBlog.Core.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Publishing;

/// <summary>One bounded row; transport keys, origins and scheduling switches have no columns here.</summary>
public sealed class PublicationSettingsConfiguration : IEntityTypeConfiguration<PublicationSettings>
{
    public void Configure(EntityTypeBuilder<PublicationSettings> e)
    {
        e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Name).HasMaxLength(80); e.Property(x => x.Cadence).HasMaxLength(200);
        e.Property(x => x.SenderEmail).HasMaxLength(254).UseCollation("C"); e.Property(x => x.SenderName).HasMaxLength(80);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.ToTable("PublicationSettings", t => t.HasCheckConstraint("CK_PublicationSettings_Valid", """
            "Id"=1 AND "Version">0 AND isfinite("UpdatedUtc")
            AND length(btrim("Name")) BETWEEN 1 AND 80 AND "Name"=btrim("Name") AND octet_length("Name")<=320 AND "Name" !~ '[[:cntrl:]<>]'
            AND length(btrim("Cadence")) BETWEEN 1 AND 200 AND "Cadence"=btrim("Cadence") AND octet_length("Cadence")<=800 AND "Cadence" !~ '[[:cntrl:]<>]'
            AND (("SenderEmail" IS NULL AND "SenderName" IS NULL) OR ("SenderEmail" IS NOT NULL AND "SenderName" IS NOT NULL
                AND length("SenderEmail") BETWEEN 3 AND 254 AND "SenderEmail" ~ '^[!-~]+@[A-Za-z0-9.-]+$'
                AND length(btrim("SenderName")) BETWEEN 1 AND 80 AND octet_length("SenderName")<=320 AND "SenderName" !~ '[[:cntrl:]<>"\\]'))
            """));
    }
}
