namespace Pos.Domain.Sales;

/// <summary>
/// Where a completed sale stands with KRA eTIMS fiscalization. <see cref="NotRequired"/> when eTIMS
/// is disabled (training / non-fiscal). Otherwise: <see cref="Signed"/> locally by the CU/VSCU →
/// <see cref="Synced"/> once transmitted to KRA, or <see cref="Failed"/> after retries are exhausted.
/// </summary>
public enum FiscalStatus { NotRequired = 0, Signed = 1, Synced = 2, Failed = 3 }
