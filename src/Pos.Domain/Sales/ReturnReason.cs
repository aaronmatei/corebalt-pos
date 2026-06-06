namespace Pos.Domain.Sales;

/// <summary>Why goods were returned. Required on every credit note.</summary>
public enum ReturnReason { Damaged = 0, WrongItem = 1, CustomerChangedMind = 2, CashierError = 3 }
