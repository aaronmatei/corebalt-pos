using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Tenancy;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever(); // UUIDv7, edge-generated
        b.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(63).IsRequired();
        b.Property(t => t.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        b.Property(t => t.PrimaryStoreId).HasColumnName("primary_store_id").IsRequired();
        b.Property(t => t.IsActive).HasColumnName("is_active");
        b.Property(t => t.SyncSecretHash).HasColumnName("sync_secret_hash").HasMaxLength(64);
        b.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        // Subdomain → tenant is a lookup on every Hq request; the slug is the natural key (one per cloud).
        b.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("ux_tenants_slug");
        b.Ignore(t => t.DomainEvents);
    }
}

internal sealed class MerchantProfileConfiguration : IEntityTypeConfiguration<MerchantProfile>
{
    public void Configure(EntityTypeBuilder<MerchantProfile> b)
    {
        b.ToTable("merchant_profiles");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(p => p.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        b.Property(p => p.TradingName).HasColumnName("trading_name").HasMaxLength(200).IsRequired();
        b.Property(p => p.KraPin).HasColumnName("kra_pin").HasMaxLength(32).IsRequired();
        b.Property(p => p.VatRegistered).HasColumnName("vat_registered");
        b.Property(p => p.VatNumber).HasColumnName("vat_number").HasMaxLength(32);
        b.Property(p => p.Phone).HasColumnName("phone").HasMaxLength(64);
        b.Property(p => p.Email).HasColumnName("email").HasMaxLength(128);
        b.Property(p => p.Address).HasColumnName("address").HasMaxLength(256);
        b.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(p => p.LogoUrl).HasColumnName("logo_url").HasMaxLength(512);
        b.Property(p => p.ReceiptFooter).HasColumnName("receipt_footer").HasMaxLength(256);
        b.Property(p => p.ShowPoweredBy).HasColumnName("show_powered_by");
        b.Property(p => p.SetupComplete).HasColumnName("setup_complete");
        b.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");

        b.HasIndex(p => p.TenantId).IsUnique().HasDatabaseName("ux_merchant_profiles_tenant");

        b.OwnsMany<Branch>("_branches", br =>
        {
            br.ToTable("branches");
            br.WithOwner().HasForeignKey("merchant_profile_id");
            br.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            br.HasKey("merchant_profile_id", "Id");
            br.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            br.Property(x => x.Code).HasColumnName("code").HasMaxLength(16);
            br.Property(x => x.Address).HasColumnName("address").HasMaxLength(256);
        });
        b.Navigation("_branches").UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(p => p.Branches);
    }
}

internal sealed class MpesaSettingsConfiguration : IEntityTypeConfiguration<MpesaSettings>
{
    public void Configure(EntityTypeBuilder<MpesaSettings> b)
    {
        b.ToTable("mpesa_settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.Enabled).HasColumnName("enabled");
        b.Property(s => s.ShortCode).HasColumnName("short_code").HasMaxLength(16);
        b.Property(s => s.ConsumerKey).HasColumnName("consumer_key").HasMaxLength(128);
        b.Property(s => s.ConsumerSecret).HasColumnName("consumer_secret"); // encrypted (text)
        b.Property(s => s.Passkey).HasColumnName("passkey");                 // encrypted (text)
        b.Property(s => s.Environment).HasColumnName("environment").HasConversion<int>();
        b.HasIndex(s => s.TenantId).IsUnique().HasDatabaseName("ux_mpesa_settings_tenant");
        b.Ignore(s => s.IsConfigured);
    }
}

internal sealed class EtimsSettingsConfiguration : IEntityTypeConfiguration<EtimsSettings>
{
    public void Configure(EntityTypeBuilder<EtimsSettings> b)
    {
        b.ToTable("etims_settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(s => s.Enabled).HasColumnName("enabled");
        b.Property(s => s.Mode).HasColumnName("mode").HasConversion<int>();
        b.Property(s => s.DeviceSerial).HasColumnName("device_serial").HasMaxLength(64);
        b.Property(s => s.BranchId).HasColumnName("branch_id").HasMaxLength(32);
        b.Property(s => s.CmcKey).HasColumnName("cmc_key");                 // encrypted (text)
        b.Property(s => s.BaseUrl).HasColumnName("base_url").HasMaxLength(256);
        b.HasIndex(s => s.TenantId).IsUnique().HasDatabaseName("ux_etims_settings_tenant");
        b.Ignore(s => s.HasRealCredentials);
    }
}

internal sealed class RegisterConfiguration : IEntityTypeConfiguration<Register>
{
    public void Configure(EntityTypeBuilder<Register> b)
    {
        b.ToTable("registers");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(r => r.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(r => r.Number).HasColumnName("number").HasMaxLength(16).IsRequired();
        b.Property(r => r.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        b.HasIndex(r => new { r.TenantId, r.StoreId }).HasDatabaseName("ix_registers_tenant_store");
        b.Ignore(r => r.DisplayLabel);
    }
}

internal sealed class OpsSettingsConfiguration : IEntityTypeConfiguration<OpsSettings>
{
    public void Configure(EntityTypeBuilder<OpsSettings> b)
    {
        b.ToTable("ops_settings");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(o => o.SecondBackupLocation).HasColumnName("second_backup_location").HasMaxLength(512);
        b.HasIndex(o => o.TenantId).IsUnique().HasDatabaseName("ux_ops_settings_tenant");
    }
}

internal sealed class PrinterProfileConfiguration : IEntityTypeConfiguration<PrinterProfile>
{
    public void Configure(EntityTypeBuilder<PrinterProfile> b)
    {
        b.ToTable("printer_profiles");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(p => p.RegisterId).HasColumnName("register_id").IsRequired();
        b.Property(p => p.Transport).HasColumnName("transport").HasConversion<int>();
        b.Property(p => p.NetworkHost).HasColumnName("network_host").HasMaxLength(128);
        b.Property(p => p.NetworkPort).HasColumnName("network_port");
        b.Property(p => p.FilePath).HasColumnName("file_path").HasMaxLength(512);
        b.Property(p => p.PaperWidth).HasColumnName("paper_width").HasConversion<int>();
        b.Property(p => p.HasCutter).HasColumnName("has_cutter");
        b.Property(p => p.HasCashDrawer).HasColumnName("has_cash_drawer");
        b.Property(p => p.NativeQrSupported).HasColumnName("native_qr_supported");
        b.HasIndex(p => new { p.TenantId, p.RegisterId }).IsUnique().HasDatabaseName("ux_printer_profiles_tenant_register");
        b.Ignore(p => p.DotWidth);
        b.Ignore(p => p.Columns);
    }
}

internal sealed class EntitlementsConfiguration : IEntityTypeConfiguration<Entitlements>
{
    public void Configure(EntityTypeBuilder<Entitlements> b)
    {
        b.ToTable("entitlements");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(e => e.Edition).HasColumnName("edition").HasConversion<int>();
        b.Property(e => e.Features).HasColumnName("features").HasConversion<int>();
        b.Property(e => e.MaxTills).HasColumnName("max_tills");
        b.Property(e => e.MaxBranches).HasColumnName("max_branches");
        b.Property(e => e.LicenseKey).HasColumnName("license_key").HasMaxLength(512);
        b.Property(e => e.ValidUntil).HasColumnName("valid_until").HasColumnType("timestamptz");
        b.HasIndex(e => e.TenantId).IsUnique().HasDatabaseName("ux_entitlements_tenant");
    }
}
