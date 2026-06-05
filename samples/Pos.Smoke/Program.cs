using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Pos.Domain.Sales;
using Pos.Domain.Inventory;

// Stand-ins for IDs that, in the real system, identify a tenant/branch/lane/cashier/products.
var tenantId = Uuid7.NewGuid();
var storeId  = Uuid7.NewGuid();   // this branch
var register = Uuid7.NewGuid();
var cashier  = Uuid7.NewGuid();
var milk     = Uuid7.NewGuid();
var tomatoes = Uuid7.NewGuid();

Console.WriteLine("== Checkout ==");
var sale = Sale.Start(tenantId, storeId, register, cashier);
sale.AddLine(milk,     "Brookside Milk 500ml", 2,      new Money(60m));   // 2 units @ 60.00
sale.AddLine(tomatoes, "Tomatoes (loose)",     0.450m, new Money(120m));  // 0.450 kg @ 120.00/kg
Console.WriteLine($"Subtotal:   {sale.Subtotal}");

sale.AddTender(TenderType.Mpesa, new Money(100m), reference: "QABC123XYZ");
sale.AddTender(TenderType.Cash,  new Money(80m));
Console.WriteLine($"Paid:       {sale.Paid}");

sale.Complete();
var change = sale.BalanceDue.Amount < 0 ? -sale.BalanceDue.Amount : 0m;
Console.WriteLine($"Change due: KES {change:0.00}");
Console.WriteLine($"Status:     {sale.Status} at {sale.CompletedAtUtc:u}");
Console.WriteLine($"Sale Id:    {sale.Id}   (time-ordered UUIDv7)");

Console.WriteLine("\n== Domain events raised (these feed the sync outbox in step 2) ==");
foreach (var e in sale.DomainEvents)
    Console.WriteLine($"  {e.GetType().Name}  event={e.EventId}  at={e.OccurredAtUtc:u}");

Console.WriteLine("\n== Inventory: stock-on-hand is the SUM of immutable movements ==");
var movements = new List<StockMovement>
{
    StockMovement.Record(tenantId, storeId, milk, +24, StockMovementReason.Purchase),
    StockMovement.Record(tenantId, storeId, milk,  -2, StockMovementReason.Sale, sourceRef: sale.Id),
};
var milkOnHand = movements.Where(m => m.ProductId == milk).Sum(m => m.QuantityDelta);
Console.WriteLine($"  Milk movements: {string.Join(", ", movements.Where(m => m.ProductId == milk).Select(m => (m.QuantityDelta > 0 ? "+" : "") + m.QuantityDelta))}");
Console.WriteLine($"  Milk on hand:   {milkOnHand}  (no quantity column was ever overwritten)");
