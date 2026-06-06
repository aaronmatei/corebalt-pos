namespace Pos.Application.Fiscalization;

/// <summary>
/// Port for KRA eTIMS fiscalization. <see cref="SignAsync"/> fiscalizes a completed sale (CU/VSCU
/// signature + CUIN + QR); <see cref="SyncAsync"/> transmits the signed invoice to KRA (the batch
/// upload). The real KRA provider drops in behind this interface; tests + dev use a fake.
/// </summary>
public interface IFiscalizationProvider
{
    Task<FiscalizationResult> SignAsync(FiscalInvoice invoice, CancellationToken ct = default);
    Task<FiscalizationResult> SyncAsync(FiscalInvoice invoice, CancellationToken ct = default);

    /// <summary>Fiscalize a CREDIT NOTE (return/void), referencing the original receipt's CUIN.</summary>
    Task<FiscalizationResult> SignCreditNoteAsync(FiscalCreditNote note, CancellationToken ct = default);
}
