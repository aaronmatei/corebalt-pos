namespace Pos.Domain.Sales;

/// <summary>
/// Lifecycle of a tender. Synchronous tenders (cash, a manually-keyed M-Pesa code) are
/// <see cref="Confirmed"/> the moment they're added. Asynchronous tenders (an M-Pesa STK push)
/// start <see cref="Pending"/> and only become <see cref="Confirmed"/> when the provider
/// confirms — or <see cref="Failed"/> if the customer cancels / times out. Only Confirmed
/// tenders count toward what a sale has been Paid.
/// </summary>
public enum TenderStatus { Pending = 0, Confirmed = 1, Failed = 2 }
