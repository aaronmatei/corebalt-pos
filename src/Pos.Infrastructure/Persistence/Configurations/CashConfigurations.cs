using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Cash;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class RegisterSessionConfiguration : IEntityTypeConfiguration<RegisterSession>
{
    public void Configure(EntityTypeBuilder<RegisterSession> b)
    {
        b.ToTable("register_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(s => s.RegisterId).HasColumnName("register_id").IsRequired();
        b.Property(s => s.RegisterLabel).HasColumnName("register_label").HasMaxLength(64);
        b.Property(s => s.OpenedBy).HasColumnName("opened_by");
        b.Property(s => s.OpenedByName).HasColumnName("opened_by_name").HasMaxLength(128);
        b.Property(s => s.OpenedAtUtc).HasColumnName("opened_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.ClosedBy).HasColumnName("closed_by");
        b.Property(s => s.ClosedByName).HasColumnName("closed_by_name").HasMaxLength(128);
        b.Property(s => s.ClosedAtUtc).HasColumnName("closed_at_utc").HasColumnType("timestamptz");
        b.Property(s => s.VarianceAcknowledged).HasColumnName("variance_acknowledged");
        b.Property(s => s.Status).HasColumnName("status").HasConversion<int>();

        b.OwnsOne(s => s.OpeningFloat, m =>
        {
            m.Property(p => p.Amount).HasColumnName("opening_float_amount").HasColumnType("numeric(18,2)");
            m.Property(p => p.Currency).HasColumnName("opening_float_currency").HasMaxLength(3);
        });
        b.Navigation(s => s.OpeningFloat).IsRequired();
        b.OwnsOne(s => s.CountedCash, m =>
        {
            m.Property(p => p.Amount).HasColumnName("counted_cash_amount").HasColumnType("numeric(18,2)");
            m.Property(p => p.Currency).HasColumnName("counted_cash_currency").HasMaxLength(3);
        });
        b.OwnsOne(s => s.ExpectedCash, m =>
        {
            m.Property(p => p.Amount).HasColumnName("expected_cash_amount").HasColumnType("numeric(18,2)");
            m.Property(p => p.Currency).HasColumnName("expected_cash_currency").HasMaxLength(3);
        });
        b.OwnsOne(s => s.Variance, m =>
        {
            m.Property(p => p.Amount).HasColumnName("variance_amount").HasColumnType("numeric(18,2)");
            m.Property(p => p.Currency).HasColumnName("variance_currency").HasMaxLength(3);
        });

        // One OPEN session per register (filtered unique index); plus partition lookups.
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.RegisterId })
            .HasFilter("status = 0").IsUnique().HasDatabaseName("ux_register_sessions_open_per_register");
        b.HasIndex(s => new { s.TenantId, s.StoreId, s.OpenedAtUtc }).HasDatabaseName("ix_register_sessions_tenant_store_opened");

        b.Ignore(s => s.DomainEvents);
        b.Ignore(s => s.IsOpen);
    }
}

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> b)
    {
        b.ToTable("cash_movements");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(m => m.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(m => m.RegisterId).HasColumnName("register_id");
        b.Property(m => m.SessionId).HasColumnName("session_id").IsRequired();
        b.Property(m => m.Type).HasColumnName("type").HasConversion<int>();
        b.Property(m => m.Reason).HasColumnName("reason").HasMaxLength(256);
        b.Property(m => m.UserId).HasColumnName("user_id");
        b.Property(m => m.UserName).HasColumnName("user_name").HasMaxLength(128);
        b.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        b.OwnsOne(m => m.Amount, mm =>
        {
            mm.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
            mm.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3);
        });
        b.Navigation(m => m.Amount).IsRequired();

        b.HasIndex(m => new { m.TenantId, m.StoreId, m.SessionId }).HasDatabaseName("ix_cash_movements_tenant_store_session");
        b.Ignore(m => m.SignedAmount);
    }
}
