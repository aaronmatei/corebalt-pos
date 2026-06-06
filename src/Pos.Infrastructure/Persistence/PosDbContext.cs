using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Payments;
using Pos.Domain.Sales;
using Pos.Infrastructure.Outbox;

namespace Pos.Infrastructure.Persistence;

public sealed class PosDbContext : DbContext, IUnitOfWork
{
    public PosDbContext(DbContextOptions<PosDbContext> options) : base(options) { }

    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<MpesaPayment> MpesaPayments => Set<MpesaPayment>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PosDbContext).Assembly);
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
