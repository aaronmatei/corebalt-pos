using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Hq;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class SyncInboxEntryConfiguration : IEntityTypeConfiguration<SyncInboxEntry>
{
    public void Configure(EntityTypeBuilder<SyncInboxEntry> b)
    {
        b.ToTable("sync_inbox");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever(); // the original store-side outbox id
        b.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.AggregateId).HasColumnName("aggregate_id").IsRequired();
        b.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(256).IsRequired();
        b.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb");
        b.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamptz");
        b.Property(e => e.EnqueuedAtUtc).HasColumnName("enqueued_at_utc").HasColumnType("timestamptz");
        b.Property(e => e.ReceivedAtUtc).HasColumnName("received_at_utc").HasColumnType("timestamptz");
        b.Property(e => e.ProjectedAtUtc).HasColumnName("projected_at_utc").HasColumnType("timestamptz");
        b.HasIndex(e => new { e.TenantId, e.StoreId, e.OccurredAtUtc }).HasDatabaseName("ix_sync_inbox_tenant_store");
    }
}

internal sealed class HqSaleConfiguration : IEntityTypeConfiguration<HqSale>
{
    public void Configure(EntityTypeBuilder<HqSale> b)
    {
        b.ToTable("hq_sales");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever(); // the original SaleId
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(s => s.ReceiptNumber).HasColumnName("receipt_number").HasMaxLength(32);
        b.Property(s => s.RegisterName).HasColumnName("register_name").HasMaxLength(64);
        b.Property(s => s.CashierName).HasColumnName("cashier_name").HasMaxLength(128);
        b.Property(s => s.CustomerId).HasColumnName("customer_id");
        b.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(s => s.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(18,2)");
        b.Property(s => s.TotalVat).HasColumnName("total_vat").HasColumnType("numeric(18,2)");
        b.Property(s => s.LineCount).HasColumnName("line_count");
        b.Property(s => s.LinesJson).HasColumnName("lines").HasColumnType("jsonb").IsRequired();
        b.Property(s => s.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.SyncedAtUtc).HasColumnName("synced_at_utc").HasColumnType("timestamptz");
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.CompletedAtUtc }).HasDatabaseName("ix_hq_sales_tenant_store_completed");
    }
}

internal sealed class HqSessionConfiguration : IEntityTypeConfiguration<HqSession>
{
    public void Configure(EntityTypeBuilder<HqSession> b)
    {
        b.ToTable("hq_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever(); // the original SessionId
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(s => s.RegisterId).HasColumnName("register_id").IsRequired();
        b.Property(s => s.RegisterLabel).HasColumnName("register_label").HasMaxLength(64);
        b.Property(s => s.OpenedByName).HasColumnName("opened_by_name").HasMaxLength(128);
        b.Property(s => s.OpenedAtUtc).HasColumnName("opened_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.OpeningFloat).HasColumnName("opening_float").HasColumnType("numeric(18,2)");
        b.Property(s => s.ClosedByName).HasColumnName("closed_by_name").HasMaxLength(128);
        b.Property(s => s.ClosedAtUtc).HasColumnName("closed_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.CountedCash).HasColumnName("counted_cash").HasColumnType("numeric(18,2)");
        b.Property(s => s.ExpectedCash).HasColumnName("expected_cash").HasColumnType("numeric(18,2)");
        b.Property(s => s.Variance).HasColumnName("variance").HasColumnType("numeric(18,2)");
        b.Property(s => s.VarianceAcknowledged).HasColumnName("variance_acknowledged");
        b.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(s => s.SyncedAtUtc).HasColumnName("synced_at_utc").HasColumnType("timestamptz");
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.ClosedAtUtc }).HasDatabaseName("ix_hq_sessions_tenant_store_closed");
    }
}

internal sealed class HqCreditNoteConfiguration : IEntityTypeConfiguration<HqCreditNote>
{
    public void Configure(EntityTypeBuilder<HqCreditNote> b)
    {
        b.ToTable("hq_credit_notes");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever(); // the original CreditNoteId
        b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(c => c.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(c => c.ReturnNumber).HasColumnName("return_number").HasMaxLength(32);
        b.Property(c => c.OriginalSaleId).HasColumnName("original_sale_id").IsRequired();
        b.Property(c => c.OriginalReceiptNumber).HasColumnName("original_receipt_number").HasMaxLength(32);
        b.Property(c => c.Reason).HasColumnName("reason").HasMaxLength(32);
        b.Property(c => c.IsVoid).HasColumnName("is_void");
        b.Property(c => c.AuthorizedByName).HasColumnName("authorized_by_name").HasMaxLength(128);
        b.Property(c => c.RefundMethod).HasColumnName("refund_method").HasMaxLength(16);
        b.Property(c => c.RefundStatus).HasColumnName("refund_status").HasMaxLength(16);
        b.Property(c => c.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(18,2)");
        b.Property(c => c.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(c => c.LineCount).HasColumnName("line_count");
        b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        b.Property(c => c.SyncedAtUtc).HasColumnName("synced_at_utc").HasColumnType("timestamptz");
        b.HasIndex(c => new { c.TenantId, c.StoreId, c.CreatedAtUtc }).HasDatabaseName("ix_hq_credit_notes_tenant_store_created");
    }
}

internal sealed class HqStockOnHandConfiguration : IEntityTypeConfiguration<HqStockOnHand>
{
    public void Configure(EntityTypeBuilder<HqStockOnHand> b)
    {
        b.ToTable("hq_stock_on_hand");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(s => s.ProductId).HasColumnName("product_id").IsRequired();
        b.Property(s => s.Sku).HasColumnName("sku").HasMaxLength(64);
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(200);
        b.Property(s => s.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(16);
        b.Property(s => s.OnHand).HasColumnName("on_hand").HasColumnType("numeric(18,3)");
        b.Property(s => s.LastMovementAtUtc).HasColumnName("last_movement_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        // One on-hand row per product per branch — the upsert key.
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.ProductId }).IsUnique().HasDatabaseName("ux_hq_stock_tenant_store_product");
    }
}
