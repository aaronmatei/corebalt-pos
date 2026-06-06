using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Sales;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("sales");
        b.HasKey(s => s.Id);

        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(s => s.RegisterId).HasColumnName("register_id").IsRequired();
        b.Property(s => s.CashierId).HasColumnName("cashier_id").IsRequired();
        b.Property(s => s.Status).HasColumnName("status").HasConversion<int>();
        b.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        b.Property(s => s.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.CreatedBy).HasColumnName("created_by");
        b.Property(s => s.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.UpdatedBy).HasColumnName("updated_by");
        b.Property(s => s.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamptz");

        // Lines and tenders are part of the Sale aggregate; they live and die with it.
        // Bind to the private backing fields; the public IReadOnly* projections are ignored
        // below so EF doesn't try to materialize them as duplicate navigations.
        // Owned collections use a composite key (sale_id, id) — that's the canonical EF Core
        // shape for owned-many. Overriding it to a single-column PK on Id makes the change
        // tracker stage new rows as Modified instead of Added (we hit that on first run).
        b.OwnsMany<SaleLine>("_lines", lines =>
        {
            lines.ToTable("sale_lines");
            lines.WithOwner().HasForeignKey("sale_id");
            lines.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
            lines.HasKey("sale_id", "Id");
            lines.Property(l => l.ProductId).HasColumnName("product_id").IsRequired();
            lines.Property(l => l.Description).HasColumnName("description").HasMaxLength(200).IsRequired();
            lines.Property(l => l.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,3)");
            lines.OwnsOne(l => l.UnitPrice, m =>
            {
                m.Property(p => p.Amount).HasColumnName("unit_price_amount").HasColumnType("numeric(18,2)");
                m.Property(p => p.Currency).HasColumnName("unit_price_currency").HasMaxLength(3);
            });
            lines.Ignore(l => l.LineTotal); // computed, not persisted
        });
        b.Navigation("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        b.OwnsMany<Tender>("_tenders", tenders =>
        {
            tenders.ToTable("tenders");
            tenders.WithOwner().HasForeignKey("sale_id");
            tenders.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            tenders.HasKey("sale_id", "Id");
            tenders.Property(t => t.Type).HasColumnName("type").HasConversion<int>();
            tenders.Property(t => t.Status).HasColumnName("status").HasConversion<int>();
            tenders.Property(t => t.Reference).HasColumnName("reference").HasMaxLength(64);
            tenders.Property(t => t.ProviderReference).HasColumnName("provider_reference").HasMaxLength(64);
            tenders.OwnsOne(t => t.Amount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
                m.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3);
            });
        });
        b.Navigation("_tenders").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Routes queries on the tenant/store partition.
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.Id }).HasDatabaseName("ix_sales_tenant_store_id");
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.Status }).HasDatabaseName("ix_sales_tenant_store_status");

        b.Ignore(s => s.DomainEvents);
        b.Ignore(s => s.Subtotal);
        b.Ignore(s => s.Paid);
        b.Ignore(s => s.BalanceDue);
        // The public read-only projections share the backing fields above — don't double-map.
        b.Ignore(s => s.Lines);
        b.Ignore(s => s.Tenders);
    }
}
