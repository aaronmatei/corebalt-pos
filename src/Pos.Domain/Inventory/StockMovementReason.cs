namespace Pos.Domain.Inventory;

public enum StockMovementReason
{
    Sale = 0, Purchase = 1, Return = 2, Adjustment = 3,
    TransferIn = 4, TransferOut = 5, Wastage = 6, OpeningBalance = 7
}
