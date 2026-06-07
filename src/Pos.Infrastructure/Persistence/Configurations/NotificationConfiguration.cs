using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Notifications;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(n => n.Id);

        b.Property(n => n.Id).HasColumnName("id");
        b.Property(n => n.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(n => n.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(n => n.Type).HasColumnName("type").HasConversion<int>();
        b.Property(n => n.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(n => n.Message).HasColumnName("message").HasMaxLength(1000).IsRequired();
        b.Property(n => n.ProductId).HasColumnName("product_id");
        b.Property(n => n.SourceMessageId).HasColumnName("source_message_id").IsRequired();
        b.Property(n => n.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        b.Property(n => n.IsRead).HasColumnName("is_read");

        // Dispatch idempotency: at most one notification per source outbox message (per store).
        b.HasIndex(n => new { n.TenantId, n.StoreId, n.SourceMessageId })
            .IsUnique()
            .HasDatabaseName("ux_notifications_source");

        // Feed read pattern: newest first within a store, with the unread badge as a partial scan.
        b.HasIndex(n => new { n.TenantId, n.StoreId, n.CreatedAtUtc })
            .HasDatabaseName("ix_notifications_feed");
    }
}
