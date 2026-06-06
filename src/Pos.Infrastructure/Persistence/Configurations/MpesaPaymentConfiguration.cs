using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Payments;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class MpesaPaymentConfiguration : IEntityTypeConfiguration<MpesaPayment>
{
    public void Configure(EntityTypeBuilder<MpesaPayment> b)
    {
        b.ToTable("mpesa_payments");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(p => p.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(p => p.SaleId).HasColumnName("sale_id").IsRequired();
        b.Property(p => p.TenderId).HasColumnName("tender_id").IsRequired();
        b.Property(p => p.CheckoutRequestId).HasColumnName("checkout_request_id").HasMaxLength(64).IsRequired();
        b.Property(p => p.MerchantRequestId).HasColumnName("merchant_request_id").HasMaxLength(64);
        b.Property(p => p.MsisdnMasked).HasColumnName("msisdn_masked").HasMaxLength(32);
        b.Property(p => p.Status).HasColumnName("status").HasConversion<int>();
        b.Property(p => p.ResultCode).HasColumnName("result_code");
        b.Property(p => p.ResultDescription).HasColumnName("result_description").HasMaxLength(256);
        b.Property(p => p.MpesaReceiptNumber).HasColumnName("mpesa_receipt").HasMaxLength(32);
        b.Property(p => p.InitiatedAtUtc).HasColumnName("initiated_at_utc").HasColumnType("timestamptz");
        b.Property(p => p.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamptz");

        b.OwnsOne(p => p.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
            m.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        // CheckoutRequestID is Daraja's globally-unique correlation key — the callback's idempotency hinge.
        b.HasIndex(p => p.CheckoutRequestId).IsUnique().HasDatabaseName("ux_mpesa_checkout_request_id");
        b.HasIndex(p => new { p.TenantId, p.StoreId, p.SaleId }).HasDatabaseName("ix_mpesa_tenant_store_sale");

        b.Ignore(p => p.DomainEvents);
    }
}
