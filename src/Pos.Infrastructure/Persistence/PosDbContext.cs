using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pos.Application.Abstractions;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Payments;
using Pos.Domain.Sales;
using Pos.Domain.Tenancy;
using Pos.Infrastructure.Outbox;
using Pos.SharedKernel;

namespace Pos.Infrastructure.Persistence;

public sealed class PosDbContext : DbContext, IUnitOfWork
{
    private readonly ISecretProtector _protector;
    private readonly ITenantProvider _tenant;

    public PosDbContext(DbContextOptions<PosDbContext> options, ISecretProtector protector, ITenantProvider? tenant = null)
        : base(options)
    {
        _protector = protector;
        _tenant = tenant ?? new NullTenantProvider(); // no request scope (design-time / direct construction) ⇒ unfiltered
    }

    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    // HQ catalogue master + change feed (M2: central catalog/pricing pushed down to stores).
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogChange> CatalogChanges => Set<CatalogChange>();
    public DbSet<CatalogPullState> CatalogPullStates => Set<CatalogPullState>();
    public DbSet<Pos.Domain.Customers.Customer> Customers => Set<Pos.Domain.Customers.Customer>();
    public DbSet<Pos.Domain.Notifications.Notification> Notifications => Set<Pos.Domain.Notifications.Notification>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>(); // M3 inter-branch transfers
    public DbSet<ReceivedTransfer> ReceivedTransfers => Set<ReceivedTransfer>(); // dest dedup of applied transfers
    public DbSet<MpesaPayment> MpesaPayments => Set<MpesaPayment>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<User> Users => Set<User>();
    public DbSet<FingerprintCredential> FingerprintCredentials => Set<FingerprintCredential>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<MerchantProfile> MerchantProfiles => Set<MerchantProfile>();
    public DbSet<MpesaSettings> MpesaSettings => Set<MpesaSettings>();
    public DbSet<EtimsSettings> EtimsSettings => Set<EtimsSettings>();
    public DbSet<Entitlements> Entitlements => Set<Entitlements>();
    public DbSet<PrinterProfile> PrinterProfiles => Set<PrinterProfile>();
    public DbSet<OpsSettings> OpsSettings => Set<OpsSettings>();
    public DbSet<Register> Registers => Set<Register>();
    public DbSet<Pos.Domain.Cash.RegisterSession> RegisterSessions => Set<Pos.Domain.Cash.RegisterSession>();
    public DbSet<Pos.Domain.Cash.CashMovement> CashMovements => Set<Pos.Domain.Cash.CashMovement>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    // HQ/cloud sync (Hq mode): durable inbox of received changes + the sales read-model projected from them.
    public DbSet<Pos.Domain.Hq.SyncInboxEntry> SyncInbox => Set<Pos.Domain.Hq.SyncInboxEntry>();
    public DbSet<Pos.Domain.Hq.HqSale> HqSales => Set<Pos.Domain.Hq.HqSale>();
    public DbSet<Pos.Domain.Hq.HqSession> HqSessions => Set<Pos.Domain.Hq.HqSession>();
    public DbSet<Pos.Domain.Hq.HqCreditNote> HqCreditNotes => Set<Pos.Domain.Hq.HqCreditNote>();
    public DbSet<Pos.Domain.Hq.HqStockOnHand> HqStockOnHand => Set<Pos.Domain.Hq.HqStockOnHand>();
    public DbSet<Pos.Domain.Hq.HqTransfer> HqTransfers => Set<Pos.Domain.Hq.HqTransfer>();
    public DbSet<Pos.Domain.Hq.HqBranch> HqBranches => Set<Pos.Domain.Hq.HqBranch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PosDbContext).Assembly);

        // Encrypt integration secrets at rest (transparent to the domain/app — they see plaintext).
        var secret = new ValueConverter<string, string>(v => _protector.Protect(v), v => _protector.Unprotect(v));
        modelBuilder.Entity<MpesaSettings>().Property(s => s.ConsumerSecret).HasConversion(secret);
        modelBuilder.Entity<MpesaSettings>().Property(s => s.Passkey).HasConversion(secret);
        modelBuilder.Entity<EtimsSettings>().Property(s => s.CmcKey).HasConversion(secret);
        // Biometric templates are encrypted at rest, same install-level key ring as the integration
        // secrets (the secret protector lives here, not in the IEntityTypeConfiguration).
        modelBuilder.Entity<FingerprintCredential>().Property(f => f.Template).HasConversion(secret);

        // Tenant-isolation safety net: every ITenantScoped entity is filtered to the current request's
        // tenant — defense in depth UNDER the repositories' explicit WHERE TenantId = … . The filter
        // short-circuits ("match all") when no request tenant is known (workers / design-time / the
        // anonymous M-Pesa callback), and the cross-tenant sync ingester opts out via IgnoreQueryFilters.
        // NOTE: Tenant (the registry) is intentionally NOT ITenantScoped, so subdomain lookup is never hidden.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.IsOwned()) continue; // owned children inherit the aggregate root's filter
            if (!typeof(ITenantScoped).IsAssignableFrom(entity.ClrType)) continue;
            SetTenantFilterMethod.MakeGenericMethod(entity.ClrType).Invoke(this, new object[] { modelBuilder });
        }
    }

    private static readonly System.Reflection.MethodInfo SetTenantFilterMethod =
        typeof(PosDbContext).GetMethod(nameof(SetTenantFilter),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    /// <summary>
    /// <c>e =&gt; !_tenant.HasTenant || e.TenantId == _tenant.TenantId</c>. Member access on the captured
    /// instance field <c>_tenant</c> is re-evaluated as a SQL parameter on every query, so one cached
    /// model serves every request and every tenant (no model-cache-key juggling needed).
    /// </summary>
    private void SetTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantScoped =>
        modelBuilder.Entity<T>().HasQueryFilter(e => !_tenant.HasTenant || e.TenantId == _tenant.TenantId);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) =>
        ExecuteInTransactionAsync(async innerCt => { await work(innerCt); return true; }, ct);
}
