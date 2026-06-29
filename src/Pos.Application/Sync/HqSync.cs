using Pos.Domain.Cash;
using Pos.Domain.Catalog;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;

namespace Pos.Application.Sync;

/// <summary>
/// On-prem (StoreServer) config for pushing this store's outbox to the HQ/cloud tier. Bound from the
/// "HqSync" section. Off by default; when enabled the push agent ships the outbox to <see cref="CloudBaseUrl"/>.
/// </summary>
public sealed class HqSyncOptions
{
    public bool Enabled { get; set; }
    public string CloudBaseUrl { get; set; } = string.Empty; // e.g. https://pos.corebalt.co.ke
    public string TenantSlug { get; set; } = string.Empty;   // this store's tenant slug in the cloud
    public string SyncToken { get; set; } = string.Empty;    // the plaintext token issued at provisioning
    public int IntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Port: ships a batch of changes to the cloud ingest endpoint and returns what it accepted.</summary>
public interface IHqSyncClient
{
    Task<SyncIngestResponse> PushAsync(SyncIngestRequest request, CancellationToken ct = default);
}

/// <summary>Maps a loaded <see cref="Sale"/> aggregate to the wire snapshot the cloud projects.</summary>
public static class SaleSnapshotFactory
{
    public static SaleSnapshot From(Sale sale)
    {
        var lines = sale.Lines.Select(l => new SaleLineSnapshot(
            l.ProductId, l.Description, l.Quantity, l.UnitPrice.Amount, l.LineTotal.Amount,
            l.TaxClass.ToString(), l.VatAmount.Amount)).ToList();

        var tenders = sale.Tenders.Select(t => new SaleTenderSnapshot(
            t.Type.ToString(), t.Amount.Amount, t.Status.ToString(), t.Reference)).ToList();

        var totalVat = sale.Lines.Sum(l => l.VatAmount.Amount);

        return new SaleSnapshot(
            sale.Id, sale.TenantId, sale.StoreId, sale.ReceiptNumber,
            sale.RegisterId, sale.RegisterName, sale.CashierId, sale.CashierName, sale.CustomerId,
            sale.Currency, sale.GrandTotal.Amount, totalVat,
            sale.CompletedAtUtc ?? sale.CreatedAtUtc, lines, tenders);
    }
}

/// <summary>Maps a closed <see cref="RegisterSession"/> to the wire snapshot the cloud projects.</summary>
public static class SessionSnapshotFactory
{
    public static SessionSnapshot From(RegisterSession s) => new(
        s.Id, s.TenantId, s.StoreId, s.RegisterId, s.RegisterLabel,
        s.OpenedByName, s.OpenedAtUtc, s.OpeningFloat.Amount,
        s.ClosedByName, s.ClosedAtUtc,
        s.CountedCash?.Amount ?? 0m, s.ExpectedCash?.Amount ?? 0m, s.Variance?.Amount ?? 0m,
        s.VarianceAcknowledged, s.OpeningFloat.Currency);
}

/// <summary>Maps an issued <see cref="CreditNote"/> to the wire snapshot the cloud projects.</summary>
public static class CreditNoteSnapshotFactory
{
    public static CreditNoteSnapshot From(CreditNote c) => new(
        c.Id, c.TenantId, c.StoreId, c.ReturnNumber, c.OriginalSaleId, c.OriginalReceiptNumber,
        c.Reason.ToString(), c.IsVoid, c.AuthorizedByName, c.RefundMethod.ToString(), c.RefundStatus.ToString(),
        c.GrandTotal.Amount, c.Currency, c.Lines.Count, c.CreatedAtUtc);
}

/// <summary>Maps a dispatched <see cref="StockTransfer"/> to the wire snapshot HQ routes (M3).</summary>
public static class TransferSnapshotFactory
{
    public static TransferSnapshot From(StockTransfer t) => new(
        t.Id, t.TenantId, t.FromStoreId, t.ToStoreId, t.ToStoreName, t.DispatchedByName, t.DispatchedAtUtc, t.Note,
        t.Lines.Select(l => new TransferLineSnapshot(l.ProductId, l.Sku, l.Name, l.Quantity)).ToList());
}

/// <summary>Builds a stock-movement snapshot from the outbox EVENT (no movement reload needed),
/// enriched with the product's Sku/Name when known so the cloud view is readable.</summary>
public static class StockMovementSnapshotFactory
{
    public static StockMovementSnapshot From(Pos.Domain.Inventory.Events.StockMovementRecorded e, Product? product, DateTimeOffset occurredAtUtc) => new(
        e.MovementId, e.TenantId, e.StoreId, e.ProductId,
        product?.Sku ?? "", product?.Name ?? "", (product?.UnitOfMeasure ?? UnitOfMeasure.Each).ToString(),
        e.QuantityDelta, e.Reason.ToString(), occurredAtUtc);
}
