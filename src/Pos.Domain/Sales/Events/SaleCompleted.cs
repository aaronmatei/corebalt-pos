using Pos.SharedKernel;

namespace Pos.Domain.Sales.Events;

public sealed record SaleCompleted(
    Guid SaleId,
    Guid TenantId,
    Guid StoreId,
    decimal Total,
    string Currency) : DomainEvent;
