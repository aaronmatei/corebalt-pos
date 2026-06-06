using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Catalog;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(p => p.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(p => p.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        b.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(p => p.UnitOfMeasure).HasColumnName("unit_of_measure").HasConversion<int>();
        b.Property(p => p.IsActive).HasColumnName("is_active");

        b.OwnsOne(p => p.Price, m =>
        {
            m.Property(x => x.Amount).HasColumnName("price_amount").HasColumnType("numeric(18,2)");
            m.Property(x => x.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });

        b.HasIndex(p => new { p.TenantId, p.StoreId, p.Sku })
            .IsUnique()
            .HasDatabaseName("ux_products_tenant_store_sku");

        b.Ignore(p => p.DomainEvents);
    }
}
