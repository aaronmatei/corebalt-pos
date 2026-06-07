using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Catalog;
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
        b.Property(s => s.RegisterName).HasColumnName("register_name").HasMaxLength(64).HasDefaultValue("");
        b.Property(s => s.RegisterSessionId).HasColumnName("register_session_id");
        b.Property(s => s.CashierId).HasColumnName("cashier_id").IsRequired();
        b.Property(s => s.CashierName).HasColumnName("cashier_name").HasMaxLength(128).HasDefaultValue("");
        b.Property(s => s.CashierStaffCode).HasColumnName("cashier_staff_code").HasMaxLength(32).HasDefaultValue("");
        b.Property(s => s.Status).HasColumnName("status").HasConversion<int>();
        b.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(s => s.ReceiptNumber).HasColumnName("receipt_number").HasMaxLength(32);

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
            lines.Property(l => l.TaxClass).HasColumnName("tax_class").HasConversion<int>();
            lines.Property(l => l.UnitOfMeasure).HasColumnName("unit_of_measure").HasConversion<int>();
            lines.OwnsOne(l => l.UnitPrice, m =>
            {
                m.Property(p => p.Amount).HasColumnName("unit_price_amount").HasColumnType("numeric(18,2)");
                m.Property(p => p.Currency).HasColumnName("unit_price_currency").HasMaxLength(3);
            });
            // VAT backed out + stored at completion (immutable).
            lines.OwnsOne(l => l.VatAmount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("vat_amount").HasColumnType("numeric(18,2)");
                m.Property(p => p.Currency).HasColumnName("vat_currency").HasMaxLength(3);
            });
            lines.OwnsOne(l => l.TaxableAmount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("taxable_amount").HasColumnType("numeric(18,2)");
                m.Property(p => p.Currency).HasColumnName("taxable_currency").HasMaxLength(3);
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

        // Per-class VAT summary, frozen at completion (one row per tax class on the sale).
        b.OwnsMany<SaleVatSummaryLine>("_vatSummary", v =>
        {
            v.ToTable("sale_vat_summary");
            v.WithOwner().HasForeignKey("sale_id");
            v.Property(x => x.TaxClass).HasColumnName("tax_class").HasConversion<int>();
            v.HasKey("sale_id", "TaxClass");
            v.OwnsOne(x => x.TaxableAmount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("taxable_amount").HasColumnType("numeric(18,2)");
                m.Property(p => p.Currency).HasColumnName("taxable_currency").HasMaxLength(3);
            });
            v.OwnsOne(x => x.VatAmount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("vat_amount").HasColumnType("numeric(18,2)");
                m.Property(p => p.Currency).HasColumnName("vat_currency").HasMaxLength(3);
            });
        });
        b.Navigation("_vatSummary").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Grand total (VAT-inclusive) frozen at completion.
        b.OwnsOne(s => s.GrandTotal, m =>
        {
            m.Property(p => p.Amount).HasColumnName("grand_total_amount").HasColumnType("numeric(18,2)");
            m.Property(p => p.Currency).HasColumnName("grand_total_currency").HasMaxLength(3);
        });

        // eTIMS fiscal fields — filled by the fiscalization seam after completion.
        b.Property(s => s.FiscalStatus).HasColumnName("fiscal_status").HasConversion<int>();
        b.Property(s => s.EtimsCuin).HasColumnName("etims_cuin").HasMaxLength(128);
        b.Property(s => s.EtimsSignature).HasColumnName("etims_signature").HasMaxLength(512);
        b.Property(s => s.EtimsQrUrl).HasColumnName("etims_qr_url").HasMaxLength(512);
        b.Property(s => s.EtimsSignedAtUtc).HasColumnName("etims_signed_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.EtimsTransmittedAtUtc).HasColumnName("etims_transmitted_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.FiscalSyncAttempts).HasColumnName("fiscal_sync_attempts");

        // Routes queries on the tenant/store partition.
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.Id }).HasDatabaseName("ix_sales_tenant_store_id");
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.Status }).HasDatabaseName("ix_sales_tenant_store_status");
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.RegisterSessionId }).HasDatabaseName("ix_sales_tenant_store_session");
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.CompletedAtUtc }).HasDatabaseName("ix_sales_tenant_store_completed");

        b.Ignore(s => s.DomainEvents);
        b.Ignore(s => s.Subtotal);
        b.Ignore(s => s.Paid);
        b.Ignore(s => s.BalanceDue);
        b.Ignore(s => s.IsFullyPaid);
        b.Ignore(s => s.HasPendingTenders);
        b.Ignore(s => s.IsFiscalized);
        // The public read-only projections share the backing fields above — don't double-map.
        b.Ignore(s => s.Lines);
        b.Ignore(s => s.Tenders);
        b.Ignore(s => s.VatSummary);
    }
}
