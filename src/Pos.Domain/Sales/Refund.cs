namespace Pos.Domain.Sales;

/// <summary>How the refund is paid out.</summary>
public enum RefundMethod { Cash = 0, Mpesa = 1, Other = 2 }

/// <summary>
/// State of the refund. Cash is <see cref="Refunded"/> immediately. M-Pesa / other are
/// <see cref="PendingManual"/> — recorded but NEVER auto-reversed (the M-Pesa reversal is a separate,
/// operationally-gated Daraja API); a human completes them out-of-band.
/// </summary>
public enum RefundStatus { Refunded = 0, PendingManual = 1 }
