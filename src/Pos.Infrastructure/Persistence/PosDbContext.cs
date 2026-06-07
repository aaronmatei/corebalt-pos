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

namespace Pos.Infrastructure.Persistence;

public sealed class PosDbContext : DbContext, IUnitOfWork
{
    private readonly ISecretProtector _protector;

    public PosDbContext(DbContextOptions<PosDbContext> options, ISecretProtector protector) : base(options)
        => _protector = protector;

    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Pos.Domain.Notifications.Notification> Notifications => Set<Pos.Domain.Notifications.Notification>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<MpesaPayment> MpesaPayments => Set<MpesaPayment>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<User> Users => Set<User>();
    public DbSet<FingerprintCredential> FingerprintCredentials => Set<FingerprintCredential>();
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
    }

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
