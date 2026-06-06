using FluentAssertions;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Domain.Tests.Ids;

public sealed class Uuid7Tests
{
    [Fact]
    public void Encodes_version_7_and_RFC4122_variant_bits()
    {
        var id = Uuid7.NewGuid();
        var bytes = id.ToByteArray();

        // Guid serializes the first 8 bytes little-endian, so the version nibble is in bytes[7]
        // and the variant in bytes[8] of the canonical big-endian form.
        var canonical = new byte[16];
        Array.Copy(bytes, canonical, 16);
        (canonical[0], canonical[3]) = (canonical[3], canonical[0]);
        (canonical[1], canonical[2]) = (canonical[2], canonical[1]);
        (canonical[4], canonical[5]) = (canonical[5], canonical[4]);
        (canonical[6], canonical[7]) = (canonical[7], canonical[6]);

        (canonical[6] >> 4).Should().Be(0x7, "INVARIANT #1 requires the version nibble = 7");
        (canonical[8] >> 6).Should().Be(0b10, "INVARIANT #1 requires RFC 4122 variant bits 10");
    }

    [Fact]
    public void Ids_minted_close_in_time_sort_in_order()
    {
        var batch = new List<Guid>();
        for (int i = 0; i < 1000; i++) batch.Add(Uuid7.NewGuid());

        var sorted = batch.OrderBy(g => g.ToString(), StringComparer.Ordinal).ToList();
        // We don't require strict per-call monotonicity (two ids inside the same ms can swap),
        // but bucketed-by-ms ordering should hold — confirm via the first 12 hex chars (48 ms bits).
        var batchBuckets = batch.Select(g => g.ToString()[..12]).ToList();
        var sortedBuckets = sorted.Select(g => g.ToString()[..12]).ToList();
        batchBuckets.Should().Equal(sortedBuckets, "the 48-bit ms prefix must be monotonic across mint-order");
    }
}
