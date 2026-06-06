using Pos.Application.Abstractions;
using Pos.Application.Fiscalization;
using Pos.Application.Inventory;
using Pos.Application.Receipts;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;

namespace Pos.Application.Sales;

/// <summary>
/// Processes a return / void as a NEW immutable CreditNote against a completed sale — never mutating
/// the original. Validates the over-return guard, writes the reversing stock-IN movements + the credit
/// note in one transaction (with a store-authoritative return number), records the refund (cash now,
/// M-Pesa/other flagged manual), and fiscalizes the credit note through the eTIMS seam. Idempotent on
/// the client-generated return id (offline replay).
/// </summary>
public sealed class ReturnService
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly ICreditNoteRepository _creditNotes;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;
    private readonly IReceiptNumberSequence _sequence;
    private readonly ReceiptNumberFormatter _formatter;
    private readonly IFiscalizationProvider _provider;
    private readonly EtimsOptions _etims;
    private readonly StoreInfo _store;

    public ReturnService(ICurrentContext ctx, ISaleRepository sales, ICreditNoteRepository creditNotes,
        IStockMovementRepository stock, IUnitOfWork uow, IReceiptNumberSequence sequence,
        ReceiptNumberFormatter formatter, IFiscalizationProvider provider, EtimsOptions etims, StoreInfo store)
    {
        _ctx = ctx;
        _sales = sales;
        _creditNotes = creditNotes;
        _stock = stock;
        _uow = uow;
        _sequence = sequence;
        _formatter = formatter;
        _provider = provider;
        _etims = etims;
        _store = store;
    }

    /// <summary>Returns the credit note, or null if the original sale isn't found in this store.</summary>
    public async Task<CreditNote?> ProcessAsync(Guid originalSaleId, Guid returnId, ReturnReason reason,
        IReadOnlyList<(Guid ProductId, decimal Quantity)> lines, RefundMethod refundMethod, CancellationToken ct = default)
    {
        // Idempotent replay: a return with this client-generated id already exists → return it unchanged.
        var existing = await _creditNotes.GetAsync(_ctx.TenantId, _ctx.StoreId, returnId, ct);
        if (existing is not null) return existing;

        var sale = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, originalSaleId, ct);
        if (sale is null) return null;
        if (sale.Status != SaleStatus.Completed)
            throw new InvalidOperationException("Only a completed sale can be returned.");
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("At least one return line is required.", nameof(lines));

        var prior = await _creditNotes.GetReturnedQuantitiesAsync(_ctx.TenantId, _ctx.StoreId, originalSaleId, ct);
        var byProduct = sale.Lines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => (Sold: g.Sum(x => x.Quantity), Line: g.First()));

        var note = CreditNote.Create(returnId, sale, reason, _ctx.UserId, _ctx.UserName, _ctx.StaffCode);

        foreach (var req in lines)
        {
            if (req.Quantity <= 0)
                throw new ArgumentException("Return quantity must be positive.", nameof(lines));
            if (!byProduct.TryGetValue(req.ProductId, out var sold))
                throw new InvalidOperationException($"Product {req.ProductId} was not on the original sale.");

            var alreadyReturned = prior.TryGetValue(req.ProductId, out var p) ? p : 0m;
            var remaining = sold.Sold - alreadyReturned;
            if (req.Quantity > remaining)
                throw new InvalidOperationException(
                    $"Cannot return {req.Quantity}: only {remaining} of {sold.Sold} remain returnable for that product.");

            note.AddLine(req.ProductId, sold.Line.Description, req.Quantity, sold.Line.UnitPrice,
                sold.Line.TaxClass, sold.Line.UnitOfMeasure);
        }

        await _uow.ExecuteInTransactionAsync(async innerCt =>
        {
            var seq = await _sequence.NextAsync(_ctx.TenantId, _ctx.StoreId, innerCt);
            note.Issue(_formatter.FormatCreditNote(seq), refundMethod);
            await _creditNotes.AddAsync(note, innerCt);

            // Reverse the sale's OUT with NEW immutable IN movements — on-hand stays derived.
            var movements = note.Lines.Select(l => StockMovement.Record(
                _ctx.TenantId, _ctx.StoreId, l.ProductId, +l.Quantity, StockMovementReason.Return,
                sourceRef: note.Id, reference: note.ReturnNumber));
            await _stock.AddRangeAsync(movements, innerCt);

            await _uow.SaveChangesAsync(innerCt);
        }, ct);

        // Fiscalize the credit note (after the commit — the provider call isn't held in the tx).
        if (_etims.Enabled && !note.IsFiscalized)
        {
            var result = await _provider.SignCreditNoteAsync(FiscalCreditNote.From(note, _store.KraPin), ct);
            if (result.Success && result.Cuin is not null && result.SignedAtUtc is not null)
                note.ApplyFiscalSignature(result.Cuin, result.Signature ?? "", result.QrData ?? "", result.SignedAtUtc.Value);
            else
                note.MarkFiscalFailed();
            await _uow.SaveChangesAsync(ct);
        }

        return note;
    }
}
