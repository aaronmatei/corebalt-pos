using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Infrastructure.Outbox;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(m => m.Id);

        b.Property(m => m.Id).HasColumnName("id");
        b.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(m => m.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(m => m.AggregateId).HasColumnName("aggregate_id").IsRequired();
        b.Property(m => m.EventType).HasColumnName("event_type").HasMaxLength(256).IsRequired();
        b.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(m => m.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamptz");
        b.Property(m => m.EnqueuedAtUtc).HasColumnName("enqueued_at_utc").HasColumnType("timestamptz");
        b.Property(m => m.ProcessedAtUtc).HasColumnName("processed_at_utc").HasColumnType("timestamptz");
        b.Property(m => m.Attempts).HasColumnName("attempts");
        b.Property(m => m.LastError).HasColumnName("last_error");

        // The dispatcher polls (processed_at_utc IS NULL) ordered by (occurred_at_utc, id);
        // partial index keeps the working set tiny once most rows are shipped.
        b.HasIndex(m => new { m.TenantId, m.StoreId, m.OccurredAtUtc })
            .HasDatabaseName("ix_outbox_pending")
            .HasFilter("processed_at_utc IS NULL");
    }
}
