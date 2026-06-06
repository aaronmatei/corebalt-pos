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
        b.Property(p => p.Barcode).HasColumnName("barcode").HasMaxLength(64);
        b.Property(p => p.UnitOfMeasure).HasColumnName("unit_of_measure").HasConversion<int>();
        b.Property(p => p.TaxClass).HasColumnName("tax_class").HasConversion<int>();
        b.Property(p => p.IsActive).HasColumnName("is_active");

        b.OwnsOne(p => p.Price, m =>
        {
            m.Property(x => x.Amount).HasColumnName("price_amount").HasColumnType("numeric(18,2)");
            m.Property(x => x.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });

        // SKU is unique per TENANT (central-catalogue model: one SKU per tenant across all branches).
        b.HasIndex(p => new { p.TenantId, p.Sku })
            .IsUnique()
            .HasDatabaseName("ux_products_tenant_sku");

        // Barcode unique per tenant, but FILTERED to non-null rows — so many products may have no
        // barcode while every real GTIN/EAN-13 stays unique. (A future multi-barcode model would move
        // these to a child table; until then this is the right constraint.)
        b.HasIndex(p => new { p.TenantId, p.Barcode })
            .IsUnique()
            .HasFilter("barcode IS NOT NULL")
            .HasDatabaseName("ux_products_tenant_barcode");

        b.Ignore(p => p.DomainEvents);
    }
}
