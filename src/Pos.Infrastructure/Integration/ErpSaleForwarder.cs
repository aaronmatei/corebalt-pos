using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Integration;
using Pos.Application.Sales;
using Pos.Domain.Sales;
using Pos.Domain.Sales.Events;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Integration;

/// <summary>
/// Drains SaleCompleted outbox rows and forwards each sale to the Corebalt ERP (this is the "real HQ
/// transport" the outbox was built for). Lives in Infrastructure because it reads the outbox table +
/// PosDbContext directly (like the OutboxDispatcher). On a successful POST the row is marked processed;
/// on failure it's left for the next pass (the ERP dedups on posSaleId, so retries are safe).
///
/// <para>NOTE: this consumes <c>OutboxMessage.ProcessedAtUtc</c> for SaleCompleted rows — it is the
/// HQ transport. Don't ALSO run the pull-based <c>/sync</c> feed for sales, or one would mark rows the
/// other never sees. (The two are alternative HQ transports.)</para>
/// </summary>
internal sealed class ErpSaleForwarder : IErpSaleForwarder
{
    private static readonly string SaleCompletedType =
        typeof(SaleCompleted).FullName ?? nameof(SaleCompleted);

    private readonly PosDbContext _db;
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IErpSaleSink _sink;
    private readonly CorebaltErpOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<ErpSaleForwarder> _log;

    public ErpSaleForwarder(PosDbContext db, ISaleRepository sales, IProductRepository products,
        IErpSaleSink sink, CorebaltErpOptions options, IClock clock, ILogger<ErpSaleForwarder> log)
    {
        _db = db;
        _sales = sales;
        _products = products;
        _sink = sink;
        _options = options;
        _clock = clock;
        _log = log;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.EventType == SaleCompletedType)
            .OrderBy(m => m.OccurredAtUtc).ThenBy(m => m.Id)
            .Take(Math.Max(1, _options.BatchSize))
            .ToListAsync(ct);

        var forwarded = 0;
        foreach (var msg in pending)
        {
            try
            {
                var sale = await _sales.GetAsync(msg.TenantId, msg.StoreId, msg.AggregateId, ct);
                if (sale is null)
                {
                    // The sale is gone (shouldn't happen — sales are immutable once completed); don't
                    // wedge the queue on it.
                    msg.MarkProcessed(_clock.UtcNow);
                }
                else
                {
                    await _sink.SendSaleAsync(await MapAsync(sale, msg, ct), ct);
                    msg.MarkProcessed(_clock.UtcNow);
                    forwarded++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                msg.MarkFailed(_clock.UtcNow, ex.Message); // leaves ProcessedAtUtc null → retried
                _log.LogWarning(ex, "Forwarding sale {SaleId} to Corebalt failed; will retry", msg.AggregateId);
            }

            await _db.SaveChangesAsync(ct);
        }

        return forwarded;
    }

    private async Task<ErpSaleDto> MapAsync(Sale sale, Outbox.OutboxMessage msg, CancellationToken ct)
    {
        var items = new List<ErpSaleLineDto>(sale.Lines.Count);
        foreach (var line in sale.Lines)
        {
            var product = await _products.GetAsync(sale.TenantId, sale.StoreId, line.ProductId, ct);
            var sku = product?.Sku ?? line.ProductId.ToString();
            // Corebalt inventory is whole units; the POS allows fractional (weighed) quantities — round.
            var qty = (int)Math.Round(line.Quantity, MidpointRounding.AwayFromZero);
            items.Add(new ErpSaleLineDto(sku, qty, line.UnitPrice.Amount));
        }

        return new ErpSaleDto(
            PosSaleId: sale.Id.ToString(),
            Items: items,
            Total: sale.GrandTotal.Amount,
            PaymentMethod: PaymentMethodOf(sale),
            OccurredAt: msg.OccurredAtUtc,
            CustomerRef: sale.CustomerId?.ToString());
    }

    // The sale's effective tender type → a Corebalt payment-method label. Mixed tenders → "MIXED".
    private static string PaymentMethodOf(Sale sale)
    {
        var confirmed = sale.Tenders.Where(t => t.IsConfirmed).ToList();
        var effective = confirmed.Count > 0 ? confirmed : sale.Tenders.ToList();
        if (effective.Count == 0) return "UNKNOWN";

        var types = effective.Select(t => t.Type).Distinct().ToList();
        if (types.Count > 1) return "MIXED";
        return types[0] switch
        {
            TenderType.Cash => "CASH",
            TenderType.Mpesa => "MPESA",
            TenderType.Card => "CARD",
            TenderType.AirtelMoney => "AIRTEL_MONEY",
            _ => types[0].ToString().ToUpperInvariant(),
        };
    }
}
