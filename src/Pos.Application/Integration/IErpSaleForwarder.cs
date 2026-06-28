namespace Pos.Application.Integration;

/// <summary>
/// One pass of the ERP-sync seam: ship not-yet-forwarded completed sales to Corebalt and mark them
/// shipped. Idempotent + retry-safe (the ERP dedups on the sale id; a failed POST leaves the row for
/// the next pass). The worker is a thin loop over this — tests can drive it directly.
/// </summary>
public interface IErpSaleForwarder
{
    /// <summary>Returns the number of sales forwarded in this pass.</summary>
    Task<int> RunOnceAsync(CancellationToken ct = default);
}
