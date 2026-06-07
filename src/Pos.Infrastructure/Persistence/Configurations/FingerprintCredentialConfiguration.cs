using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Identity;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class FingerprintCredentialConfiguration : IEntityTypeConfiguration<FingerprintCredential>
{
    public void Configure(EntityTypeBuilder<FingerprintCredential> b)
    {
        b.ToTable("user_fingerprints");
        b.HasKey(f => f.Id);

        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.UserId).HasColumnName("user_id").IsRequired();
        // Template is the SDK's extracted minutiae as base64; ENCRYPTED at rest (converter in
        // PosDbContext.OnModelCreating). No max length — encrypted templates are sizeable.
        b.Property(f => f.Template).HasColumnName("template").IsRequired();
        b.Property(f => f.FingerLabel).HasColumnName("finger_label").HasMaxLength(64);
        b.Property(f => f.EnrolledByUserId).HasColumnName("enrolled_by_user_id").IsRequired();
        b.Property(f => f.EnrolledAtUtc).HasColumnName("enrolled_at_utc").HasColumnType("timestamptz");
        b.Property(f => f.ConsentGiven).HasColumnName("consent_given");
        b.Property(f => f.ConsentRecordedAtUtc).HasColumnName("consent_recorded_at_utc").HasColumnType("timestamptz");
        b.Ignore(f => f.TemplateBytes);

        b.HasIndex(f => f.UserId).HasDatabaseName("ix_user_fingerprints_user");
    }
}
