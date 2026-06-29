using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Inventory;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("stock_transfers");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(t => t.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(t => t.FromStoreId).HasColumnName("from_store_id").IsRequired();
        b.Property(t => t.ToStoreId).HasColumnName("to_store_id").IsRequired();
        b.Property(t => t.ToStoreName).HasColumnName("to_store_name").HasMaxLength(128);
        b.Property(t => t.Status).HasColumnName("status").HasConversion<int>();
        b.Property(t => t.DispatchedBy).HasColumnName("dispatched_by");
        b.Property(t => t.DispatchedByName).HasColumnName("dispatched_by_name").HasMaxLength(128);
        b.Property(t => t.DispatchedAtUtc).HasColumnName("dispatched_at_utc").HasColumnType("timestamptz");
        b.Property(t => t.Note).HasColumnName("note").HasMaxLength(256);

        b.OwnsMany<StockTransferLine>("_lines", lines =>
        {
            lines.ToTable("stock_transfer_lines");
            lines.WithOwner().HasForeignKey("stock_transfer_id");
            lines.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            lines.HasKey("stock_transfer_id", "Id");
            lines.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
            lines.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64);
            lines.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            lines.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,3)");
        });
        b.Navigation("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(t => t.Lines);
        b.Ignore(t => t.DomainEvents);

        b.HasIndex(t => new { t.TenantId, t.StoreId, t.DispatchedAtUtc }).HasDatabaseName("ix_stock_transfers_tenant_store");
    }
}

internal sealed class ReceivedTransferConfiguration : IEntityTypeConfiguration<ReceivedTransfer>
{
    public void Configure(EntityTypeBuilder<ReceivedTransfer> b)
    {
        b.ToTable("received_transfers");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(r => r.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(r => r.TransferId).HasColumnName("transfer_id").IsRequired();
        b.Property(r => r.AppliedAtUtc).HasColumnName("applied_at_utc").HasColumnType("timestamptz");
        b.HasIndex(r => new { r.TenantId, r.StoreId, r.TransferId }).IsUnique().HasDatabaseName("ux_received_transfers");
    }
}
