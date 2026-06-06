using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Fiscalization;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Application.Payments;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Infrastructure.Identity;
using Pos.Infrastructure.Mpesa;
using Pos.Infrastructure.Outbox;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Persistence.Repositories;
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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
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
        services.AddScoped<IMpesaPaymentRepository, MpesaPaymentRepository>();
        services.AddScoped<IReceiptNumberSequence, ReceiptNumberSequence>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();

        // Lightweight identity: password hashing + user persistence (JWT issuer is wired in the host,
        // which owns the signing key config).
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();

        // Tenancy: merchant profile + per-tenant integration settings (encrypted) + entitlements + setup.
        // Secret protector defaults here; the host overrides it with the install's configured key.
        services.AddSingleton<ISecretProtector>(new AesSecretProtector("corebalt-default-dev-key-please-override"));
        services.AddScoped<IMerchantProfileRepository, MerchantProfileRepository>();
        services.AddScoped<IMpesaSettingsRepository, MpesaSettingsRepository>();
        services.AddScoped<IEtimsSettingsRepository, EtimsSettingsRepository>();
        services.AddScoped<IEntitlementsRepository, EntitlementsRepository>();
        services.AddScoped<IEntitlements, EntitlementsService>();
        services.AddScoped<ISetupGuard, SetupGuard>();
        services.AddScoped<SetupService>();
        services.AddScoped<MpesaSettingsResolver>();
        services.AddSingleton(new EtimsWorkerOptions());

        return services;
    }
}
