using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Abstractions;
using Pos.Application.Cash;
using Pos.Application.Catalog;
using Pos.Application.Fiscalization;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Application.Licensing;
using Pos.Application.Payments;
using Pos.Application.Printing;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Infrastructure.Identity;
using Pos.Application.Notifications;
using Pos.Infrastructure.Mpesa;
using Pos.Infrastructure.Notifications;
using Pos.Infrastructure.Outbox;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Persistence.Repositories;
using Pos.Infrastructure.Printing;
using Pos.Infrastructure.Security;

namespace Pos.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires Pos.Infrastructure into a host. Call from the composition root (the API in step 3
    /// or the persistence demo sample). The connection string targets the per-store Postgres
    /// instance — each branch's store server has its own database; HQ aggregates from outbox
    /// shipments.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString,
        string? dataProtectionKeysPath = null)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<DomainEventsToOutboxInterceptor>();

        services.AddDbContext<PosDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations"));
            opts.AddInterceptors(sp.GetRequiredService<DomainEventsToOutboxInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PosDbContext>());
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<ICreditNoteRepository, CreditNoteRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<Pos.Application.Customers.ICustomerRepository, CustomerRepository>();
        services.AddScoped<Pos.Application.Customers.CustomerService>();
        // Default loyalty rule so design-time/console hosts can construct CheckoutService; the API host
        // re-registers a config-bound instance after AddInfrastructure (last registration wins).
        services.AddSingleton(new Pos.Application.Customers.LoyaltyOptions());
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IMpesaPaymentRepository, MpesaPaymentRepository>();
        services.AddScoped<IReceiptNumberSequence, ReceiptNumberSequence>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
        services.AddScoped<Pos.Application.Sync.IOutboxSyncStore, Pos.Infrastructure.Sync.OutboxSyncStore>();

        // Notifications: the in-app channel is always on; Email/SMS are stubs (disabled until configured).
        // The dispatcher reads ProductLowStock outbox rows and fans them out to every enabled channel.
        // Default (disabled) channel options — the host may rebind these from per-client config.
        services.AddSingleton(new EmailChannelOptions());
        services.AddSingleton(new SmsChannelOptions());
        // Real-time push default = no-op (the feed still persists); the API host swaps in the SignalR one.
        services.AddSingleton<Pos.Application.Notifications.INotificationBroadcaster, NullNotificationBroadcaster>();
        services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<INotificationChannel, SmsNotificationChannel>();
        services.AddScoped<INotificationDispatcher, LowStockNotificationDispatcher>();

        // Lightweight identity: password hashing + user persistence (JWT issuer is wired in the host,
        // which owns the signing key config).
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();

        // Fingerprint auth (OPTIONAL — PIN stays the fallback): a stub reader SDK for dev/test; the real
        // reader (DigitalPersona/ZKTeco/SecuGen/Futronic) drops in behind IFingerprintAuthenticator. The
        // host may rebind FingerprintOptions from config. FingerprintService itself is wired in the host
        // (it needs the JWT issuer + StoreServer identity), like AuthService.
        services.AddSingleton(new FingerprintOptions());
        services.AddSingleton<IFingerprintAuthenticator, StubFingerprintAuthenticator>();

        // Tenancy: merchant profile + per-tenant integration settings (encrypted) + entitlements + setup.
        // Integration secrets are encrypted at rest with ASP.NET Core Data Protection — the install-level
        // key ring persisted to disk, isolated by application name. The host passes the per-install path
        // (the service account can read it); falls back to LocalAppData for dev/tests. No app-config key.
        var keysDir = string.IsNullOrWhiteSpace(dataProtectionKeysPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CorebaltPos", "dp-keys")
            : dataProtectionKeysPath;
        Directory.CreateDirectory(keysDir);
        services.AddDataProtection()
            .SetApplicationName("Corebalt.POS")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Licensing: the app only ever VERIFIES (embedded public key). Entitlements derive from the key.
        services.AddSingleton<ILicenseVerifier, LicenseVerifier>();

        services.AddScoped<IMerchantProfileRepository, MerchantProfileRepository>();
        services.AddScoped<IMpesaSettingsRepository, MpesaSettingsRepository>();
        services.AddScoped<IEtimsSettingsRepository, EtimsSettingsRepository>();
        services.AddScoped<IEntitlementsRepository, EntitlementsRepository>();
        services.AddScoped<IRegisterRepository, RegisterRepository>();
        services.AddScoped<IOpsSettingsRepository, OpsSettingsRepository>();
        services.AddScoped<IEntitlements, EntitlementsService>();

        // Cash management + close-of-day: register shifts, drawer movements, X/Z report projections.
        services.AddScoped<IRegisterSessionRepository, RegisterSessionRepository>();
        services.AddScoped<ICashMovementRepository, CashMovementRepository>();
        services.AddScoped<CashOfficeService>();
        services.AddScoped<CashOfficeReportService>();
        services.AddScoped<Pos.Application.Reports.VatReportService>();
        services.AddSingleton(new CashOfficeOptions());
        services.AddSingleton(new Pos.Application.Receipts.ReceiptOptions()); // report VAT codes/labels (host may override)
        services.AddScoped<ISetupGuard, SetupGuard>();
        services.AddScoped<SetupService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<MpesaSettingsResolver>();
        services.AddSingleton(new EtimsWorkerOptions());

        // Thermal printing: per-register profile, ESC/POS builder, visual preview, and the printer
        // router (Null/File/Network selected by the profile's transport — File/Null are the dev defaults).
        services.AddScoped<IPrinterProfileRepository, PrinterProfileRepository>();
        services.AddSingleton<IEscPosBuilder, EscPosBuilder>();
        services.AddSingleton<IReceiptPreviewRenderer, ReceiptPreviewRenderer>();
        services.AddSingleton<NullPrinter>();
        services.AddSingleton<EscPosFilePrinter>();
        services.AddSingleton<EscPosNetworkPrinter>();
        services.AddSingleton<IReceiptPrinter, ReceiptPrinterRouter>();

        return services;
    }
}
