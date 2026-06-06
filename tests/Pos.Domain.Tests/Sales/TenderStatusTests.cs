using FluentAssertions;
using Pos.Domain.Sales;
using Pos.Domain.Sales.Events;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Domain.Tests.Sales;

/// <summary>
/// Async-tender lifecycle: M-Pesa is a pending→confirmed/failed flow, never a synchronous tender.
/// Only Confirmed tenders count toward Paid, and a sale can't complete while a tender is Pending.
/// </summary>
public sealed class TenderStatusTests
{
    private static Sale SaleWithLine(decimal lineTotal = 100m)
    {
        var sale = Sale.Start(Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid());
        sale.AddLine(Uuid7.NewGuid(), "Item", 1, new Money(lineTotal));
        return sale;
    }

    [Fact]
    public void Cash_tender_is_confirmed_on_creation_and_counts_as_paid()
    {
        var sale = SaleWithLine(100m);
        sale.AddTender(TenderType.Cash, new Money(100m));

        sale.Tenders.Should().ContainSingle().Which.Status.Should().Be(TenderStatus.Confirmed);
        sale.Paid.Amount.Should().Be(100m);
        sale.HasPendingTenders.Should().BeFalse();
        sale.IsFullyPaid.Should().BeTrue();
    }

    [Fact]
    public void Pending_tender_does_not_count_as_paid_and_blocks_completion()
    {
        var sale = SaleWithLine(100m);
        sale.AddPendingTender(TenderType.Mpesa, new Money(100m));

        sale.Paid.Amount.Should().Be(0m, "a pending STK push is not money in hand");
        sale.BalanceDue.Amount.Should().Be(100m);
        sale.HasPendingTenders.Should().BeTrue();

        Action act = () => sale.Complete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*pending*");
    }

    [Fact]
    public void Confirming_a_pending_tender_makes_the_sale_completable()
    {
        var sale = SaleWithLine(100m);
        var tenderId = sale.AddPendingTender(TenderType.Mpesa, new Money(100m));

        sale.ConfirmTender(tenderId, "QABC123XYZ");

        sale.Paid.Amount.Should().Be(100m);
        sale.IsFullyPaid.Should().BeTrue();
        sale.Tenders.Single().Reference.Should().Be("QABC123XYZ");

        sale.Complete();
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.DomainEvents.Should().ContainSingle(e => e is SaleCompleted);
    }

    [Fact]
    public void Failed_tender_does_not_count_and_leaves_the_sale_unpaid()
    {
        var sale = SaleWithLine(100m);
        var tenderId = sale.AddPendingTender(TenderType.Mpesa, new Money(100m));

        sale.FailTender(tenderId);

        sale.Tenders.Single().Status.Should().Be(TenderStatus.Failed);
        sale.Paid.Amount.Should().Be(0m);
        sale.HasPendingTenders.Should().BeFalse("a failed tender is terminal, not in-flight");

        Action act = () => sale.Complete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*not fully paid*");
    }

    [Fact]
    public void A_failed_tender_cannot_be_confirmed()
    {
        var sale = SaleWithLine();
        var tenderId = sale.AddPendingTender(TenderType.Mpesa, new Money(100m));
        sale.FailTender(tenderId);

        Action act = () => sale.ConfirmTender(tenderId);
        act.Should().Throw<InvalidOperationException>().WithMessage("*failed*");
    }

    [Fact]
    public void A_confirmed_tender_cannot_be_failed()
    {
        var sale = SaleWithLine();
        var tenderId = sale.AddPendingTender(TenderType.Mpesa, new Money(100m));
        sale.ConfirmTender(tenderId);

        Action act = () => sale.FailTender(tenderId);
        act.Should().Throw<InvalidOperationException>().WithMessage("*confirmed*");
    }

    [Fact]
    public void Confirming_twice_is_idempotent()
    {
        var sale = SaleWithLine();
        var tenderId = sale.AddPendingTender(TenderType.Mpesa, new Money(100m));

        sale.ConfirmTender(tenderId, "RCPT1");
        sale.ConfirmTender(tenderId); // no-op; keeps original receipt

        sale.Tenders.Single().Status.Should().Be(TenderStatus.Confirmed);
        sale.Tenders.Single().Reference.Should().Be("RCPT1");
    }

    [Fact]
    public void Mixed_cash_and_pending_mpesa_completes_only_after_mpesa_confirms()
    {
        var sale = SaleWithLine(200m);
        sale.AddTender(TenderType.Cash, new Money(50m));                       // confirmed now
        var mpesaId = sale.AddPendingTender(TenderType.Mpesa, new Money(150m)); // pending

        sale.Paid.Amount.Should().Be(50m, "only the confirmed cash portion counts yet");
        Action tooEarly = () => sale.Complete();
        tooEarly.Should().Throw<InvalidOperationException>().WithMessage("*pending*");

        sale.ConfirmTender(mpesaId, "QXYZ");
        sale.Paid.Amount.Should().Be(200m);
        sale.IsFullyPaid.Should().BeTrue();

        sale.Complete();
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.DomainEvents.OfType<SaleCompleted>().Should().ContainSingle();
    }

    [Fact]
    public void Pending_tender_carries_a_settable_provider_reference()
    {
        var sale = SaleWithLine();
        var tenderId = sale.AddPendingTender(TenderType.Mpesa, new Money(100m));

        sale.SetTenderProviderReference(tenderId, "ws_CO_123456");

        sale.Tenders.Single().ProviderReference.Should().Be("ws_CO_123456");
        sale.Tenders.Single().Status.Should().Be(TenderStatus.Pending);
    }
}
