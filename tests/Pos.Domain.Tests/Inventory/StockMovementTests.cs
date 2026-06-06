using FluentAssertions;
using Pos.Domain.Inventory;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Domain.Tests.Inventory;

public sealed class StockMovementTests
{
    [Fact]
    public void Record_rejects_zero_delta()
    {
        Action act = () => StockMovement.Record(
            Uuid7.NewGuid(), Uuid7.NewGuid(), Uuid7.NewGuid(),
            quantityDelta: 0, reason: StockMovementReason.Adjustment);

        act.Should().Throw<ArgumentException>(
            "a zero movement carries no fact — that's the kind of overwrite-as-no-op we are avoiding");
    }

    [Fact]
    public void Record_carries_tenant_store_and_occurred_timestamp()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var product = Uuid7.NewGuid();

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var m = StockMovement.Record(tenant, store, product, +5, StockMovementReason.Purchase);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        m.TenantId.Should().Be(tenant);
        m.StoreId.Should().Be(store);
        m.ProductId.Should().Be(product);
        m.QuantityDelta.Should().Be(5);
        m.Reason.Should().Be(StockMovementReason.Purchase);
        m.OccurredAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void StockOnHand_is_the_sum_of_movements_no_field_overwritten()
    {
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        var milk = Uuid7.NewGuid();
        var saleId = Uuid7.NewGuid();

        var movements = new[]
        {
            StockMovement.Record(tenant, store, milk, +24, StockMovementReason.Purchase),
            StockMovement.Record(tenant, store, milk, -2,  StockMovementReason.Sale, sourceRef: saleId),
            StockMovement.Record(tenant, store, milk, -1,  StockMovementReason.Wastage),
            StockMovement.Record(tenant, store, milk, +6,  StockMovementReason.Return),
        };

        var onHand = movements.Sum(m => m.QuantityDelta);

        onHand.Should().Be(27,
            "INVARIANT #3 — stock-on-hand is derived from append-only facts; no row is ever overwritten");
        movements.Select(m => m.Id).Distinct().Should().HaveCount(movements.Length,
            "each movement gets its own UUIDv7 id");
    }
}
