using FluentAssertions;
using Pos.SharedKernel;
using Xunit;

namespace Pos.Domain.Tests.SharedKernel;

public sealed class MoneyTests
{
    [Fact]
    public void Add_with_mismatched_currency_throws()
    {
        var kes = new Money(100m, "KES");
        var usd = new Money(1m, "USD");
        Action act = () => kes.Add(usd);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Currency mismatch*");
    }

    [Theory]
    [InlineData(1.005, 1.00)] // banker's rounding: 1.005 → 1.00
    [InlineData(1.015, 1.02)] // 1.015 → 1.02
    [InlineData(2.675, 2.68)] // 2.675 → 2.68
    public void Amount_uses_bankers_rounding_to_two_dp(double input, double expected)
    {
        new Money((decimal)input).Amount.Should().Be((decimal)expected);
    }
}
