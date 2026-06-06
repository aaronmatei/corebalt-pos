using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptCounterConfiguration : IEntityTypeConfiguration<ReceiptCounter>
{
    public void Configure(EntityTypeBuilder<ReceiptCounter> b)
    {
        b.ToTable("receipt_counters");
        // Composite PK gives the unique constraint the atomic INSERT ... ON CONFLICT relies on.
        b.HasKey(c => new { c.TenantId, c.StoreId });
        b.Property(c => c.TenantId).HasColumnName("tenant_id");
        b.Property(c => c.StoreId).HasColumnName("store_id");
        b.Property(c => c.NextValue).HasColumnName("next_value");
    }
}
