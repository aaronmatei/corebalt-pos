using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Catalog;

namespace Pos.Infrastructure.Persistence.Configurations;

/// <summary>HQ/cloud catalogue master (M2). Tenant-scoped; SKU is the natural key per tenant.</summary>
internal sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> b)
    {
        b.ToTable("catalog_items");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(c => c.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        b.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(c => c.Barcode).HasColumnName("barcode").HasMaxLength(64);
        b.Property(c => c.UnitOfMeasure).HasColumnName("unit_of_measure").HasConversion<int>();
        b.Property(c => c.TaxClass).HasColumnName("tax_class").HasConversion<int>();
        b.Property(c => c.IsActive).HasColumnName("is_active");
        b.Property(c => c.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        b.OwnsOne(c => c.Price, m =>
        {
            m.Property(x => x.Amount).HasColumnName("price_amount").HasColumnType("numeric(18,2)");
            m.Property(x => x.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });
        b.HasIndex(c => new { c.TenantId, c.Sku }).IsUnique().HasDatabaseName("ux_catalog_items_tenant_sku");
        b.Ignore(c => c.DomainEvents);
    }
}

/// <summary>Append-only catalogue change feed (M2) — the cursor source stores pull from. Monotonic
/// DB-assigned <c>seq</c> is the cursor; full snapshot per row.</summary>
internal sealed class CatalogChangeConfiguration : IEntityTypeConfiguration<CatalogChange>
{
    public void Configure(EntityTypeBuilder<CatalogChange> b)
    {
        b.ToTable("catalog_changes");
        b.HasKey(c => c.Seq);
        b.Property(c => c.Seq).HasColumnName("seq").UseIdentityByDefaultColumn(); // monotonic cursor
        b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(c => c.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        b.Property(c => c.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        b.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(c => c.PriceAmount).HasColumnName("price_amount").HasColumnType("numeric(18,2)");
        b.Property(c => c.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(c => c.TaxClass).HasColumnName("tax_class").HasMaxLength(16).IsRequired();
        b.Property(c => c.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(16).IsRequired();
        b.Property(c => c.Barcode).HasColumnName("barcode").HasMaxLength(64);
        b.Property(c => c.IsActive).HasColumnName("is_active");
        b.Property(c => c.ChangedAtUtc).HasColumnName("changed_at_utc").HasColumnType("timestamptz");
        b.HasIndex(c => new { c.TenantId, c.Seq }).HasDatabaseName("ix_catalog_changes_tenant_seq");
    }
}

/// <summary>Store-side cursor into the HQ catalogue feed (M2). One row per (tenant, store).</summary>
internal sealed class CatalogPullStateConfiguration : IEntityTypeConfiguration<CatalogPullState>
{
    public void Configure(EntityTypeBuilder<CatalogPullState> b)
    {
        b.ToTable("catalog_pull_state");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(s => s.LastSeq).HasColumnName("last_seq");
        b.Property(s => s.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        b.HasIndex(s => new { s.TenantId, s.StoreId }).IsUnique().HasDatabaseName("ux_catalog_pull_state_tenant_store");
    }
}
