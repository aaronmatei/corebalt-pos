using FluentAssertions;
using Pos.Domain.Sales;
using Pos.Domain.Sales.Events;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Domain.Tests.Sales;

public sealed class SaleTests
{
    private static Sale NewSale() =>
        Sale.Start(Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid());

    [Fact]
    public void Start_carries_tenant_and_store_ids()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var sale = Sale.Start(tenant, store, Uuid7.NewGuid(), Uuid7.NewGuid());

        sale.TenantId.Should().Be(tenant, "INVARIANT #4 — every fact is tenant-scoped from row one");
        sale.StoreId.Should().Be(store, "INVARIANT #2 — the owning branch is recorded on every fact");
        sale.Status.Should().Be(SaleStatus.Open);
    }

    [Fact]
    public void Cannot_complete_empty_sale()
    {
        var sale = NewSale();
        Action act = () => sale.Complete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*empty*");
    }

    [Fact]
    public void Cannot_complete_underpaid_sale()
    {
        var sale = NewSale();
        sale.AddLine(Uuid7.NewGuid(), "Milk", 1, new Money(60m));
        sale.AddTender(TenderType.Cash, new Money(30m));
        Action act = () => sale.Complete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*not fully paid*");
    }

    [Fact]
    public void Complete_raises_SaleCompleted_with_total_and_scope()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var sale = Sale.Start(tenant, store, Uuid7.NewGuid(), Uuid7.NewGuid());
        sale.AddLine(Uuid7.NewGuid(), "Milk", 2, new Money(60m));   // 120
        sale.AddLine(Uuid7.NewGuid(), "Tomatoes", 0.5m, new Money(120m)); // 60
        sale.AddTender(TenderType.Cash, new Money(180m));

        sale.Complete();

        sale.Status.Should().Be(SaleStatus.Completed);
        sale.CompletedAtUtc.Should().NotBeNull();
        sale.DomainEvents.Should().ContainSingle(e => e is SaleCompleted);

        var evt = (SaleCompleted)sale.DomainEvents.Single();
        evt.SaleId.Should().Be(sale.Id);
        evt.TenantId.Should().Be(tenant);
        evt.StoreId.Should().Be(store);
        evt.Total.Should().Be(180m);
        evt.Currency.Should().Be("KES");
    }

    [Fact]
    public void Cannot_add_line_to_completed_sale()
    {
        var sale = NewSale();
        sale.AddLine(Uuid7.NewGuid(), "Milk", 1, new Money(60m));
        sale.AddTender(TenderType.Cash, new Money(60m));
        sale.Complete();

        Action act = () => sale.AddLine(Uuid7.NewGuid(), "Bread", 1, new Money(50m));
        act.Should().Throw<InvalidOperationException>(
            "INVARIANT #3 — completed sales are immutable; corrections go through new records");
    }

    [Fact]
    public void Line_currency_must_match_sale_currency()
    {
        var sale = Sale.Start(Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid(), "KES");
        Action act = () => sale.AddLine(Uuid7.NewGuid(), "Milk", 1, new Money(1m, "USD"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*currency mismatch*");
    }

    [Fact]
    public void Parked_sale_can_resume_but_not_complete()
    {
        var sale = NewSale();
        sale.AddLine(Uuid7.NewGuid(), "Milk", 1, new Money(60m));
        sale.Park();

        sale.Status.Should().Be(SaleStatus.Parked);
        Action complete = () => sale.Complete();
        complete.Should().Throw<InvalidOperationException>();

        sale.Resume();
        sale.Status.Should().Be(SaleStatus.Open);
    }
}
