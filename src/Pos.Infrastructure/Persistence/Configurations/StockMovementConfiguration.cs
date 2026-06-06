using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Inventory;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements");
        b.HasKey(m => m.Id);

        b.Property(m => m.Id).HasColumnName("id");
        b.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(m => m.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(m => m.ProductId).HasColumnName("product_id").IsRequired();
        b.Property(m => m.QuantityDelta).HasColumnName("quantity_delta").HasColumnType("numeric(18,3)");
        b.Property(m => m.Reason).HasColumnName("reason").HasConversion<int>();
        b.Property(m => m.SourceRef).HasColumnName("source_ref");
        b.Property(m => m.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamptz");

        // Drives the stock-on-hand SUM query (the only allowed way to ask "how much do we have").
        b.HasIndex(m => new { m.TenantId, m.StoreId, m.ProductId })
            .HasDatabaseName("ix_stock_movements_tenant_store_product");
    }
}
