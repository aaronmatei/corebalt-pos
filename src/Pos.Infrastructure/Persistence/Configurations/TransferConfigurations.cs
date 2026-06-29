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

internal sealed class IncomingTransferConfiguration : IEntityTypeConfiguration<IncomingTransfer>
{
    public void Configure(EntityTypeBuilder<IncomingTransfer> b)
    {
        b.ToTable("incoming_transfers");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();   // id == the transfer id (stable across branches)
        b.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(t => t.StoreId).HasColumnName("store_id").IsRequired(); // the destination store
        b.Property(t => t.FromStoreId).HasColumnName("from_store_id").IsRequired();
        b.Property(t => t.FromStoreName).HasColumnName("from_store_name").HasMaxLength(128);
        b.Property(t => t.DispatchedByName).HasColumnName("dispatched_by_name").HasMaxLength(128);
        b.Property(t => t.DispatchedAtUtc).HasColumnName("dispatched_at_utc").HasColumnType("timestamptz");
        b.Property(t => t.Note).HasColumnName("note").HasMaxLength(256);
        b.Property(t => t.Status).HasColumnName("status").HasConversion<int>();
        b.Property(t => t.ReceivedAtUtc).HasColumnName("received_at_utc").HasColumnType("timestamptz");
        b.Property(t => t.ReceivedByName).HasColumnName("received_by_name").HasMaxLength(128);

        b.OwnsMany<IncomingTransferLine>("_lines", lines =>
        {
            lines.ToTable("incoming_transfer_lines");
            lines.WithOwner().HasForeignKey("incoming_transfer_id");
            lines.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            lines.HasKey("incoming_transfer_id", "Id");
            lines.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64);
            lines.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            lines.Property(x => x.ExpectedQuantity).HasColumnName("expected_quantity").HasColumnType("numeric(18,3)");
            lines.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").HasColumnType("numeric(18,3)");
            lines.Ignore(x => x.Discrepancy);
        });
        b.Navigation("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(t => t.Lines);
        b.Ignore(t => t.HasDiscrepancy);

        b.HasIndex(t => new { t.TenantId, t.StoreId, t.Status }).HasDatabaseName("ix_incoming_transfers_tenant_store_status");
    }
}
