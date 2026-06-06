using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Sales;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.ToTable("credit_notes");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(c => c.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(c => c.OriginalSaleId).HasColumnName("original_sale_id").IsRequired();
        b.Property(c => c.OriginalReceiptNumber).HasColumnName("original_receipt_number").HasMaxLength(32);
        b.Property(c => c.OriginalEtimsCuin).HasColumnName("original_etims_cuin").HasMaxLength(128);
        b.Property(c => c.Reason).HasColumnName("reason").HasConversion<int>();
        b.Property(c => c.AuthorizedBy).HasColumnName("authorized_by");
        b.Property(c => c.AuthorizedByName).HasColumnName("authorized_by_name").HasMaxLength(128);
        b.Property(c => c.AuthorizedByStaffCode).HasColumnName("authorized_by_staff_code").HasMaxLength(32);
        b.Property(c => c.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(c => c.RefundMethod).HasColumnName("refund_method").HasConversion<int>();
        b.Property(c => c.RefundStatus).HasColumnName("refund_status").HasConversion<int>();
        b.Property(c => c.ReturnNumber).HasColumnName("return_number").HasMaxLength(32);
        b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");

        b.OwnsOne(c => c.RefundAmount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("refund_amount").HasColumnType("numeric(18,2)");
            m.Property(p => p.Currency).HasColumnName("refund_currency").HasMaxLength(3);
        });
        b.OwnsOne(c => c.GrandTotal, m =>
        {
            m.Property(p => p.Amount).HasColumnName("grand_total_amount").HasColumnType("numeric(18,2)");
            m.Property(p => p.Currency).HasColumnName("grand_total_currency").HasMaxLength(3);
        });

        b.OwnsMany<CreditNoteLine>("_lines", lines =>
        {
            lines.ToTable("credit_note_lines");
            lines.WithOwner().HasForeignKey("credit_note_id");
            lines.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
            lines.HasKey("credit_note_id", "Id");
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
            lines.Ignore(l => l.LineTotal);
        });
        b.Navigation("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Credit-note fiscal fields (stub via the eTIMS seam).
        b.Property(c => c.FiscalStatus).HasColumnName("fiscal_status").HasConversion<int>();
        b.Property(c => c.EtimsCuin).HasColumnName("etims_cuin").HasMaxLength(128);
        b.Property(c => c.EtimsSignature).HasColumnName("etims_signature").HasMaxLength(512);
        b.Property(c => c.EtimsQrUrl).HasColumnName("etims_qr_url").HasMaxLength(512);
        b.Property(c => c.EtimsSignedAtUtc).HasColumnName("etims_signed_at_utc").HasColumnType("timestamptz");

        b.HasIndex(c => new { c.TenantId, c.StoreId, c.OriginalSaleId }).HasDatabaseName("ix_credit_notes_tenant_store_sale");

        b.Ignore(c => c.DomainEvents);
        b.Ignore(c => c.IsFiscalized);
        b.Ignore(c => c.Lines);
    }
}
