using System.Security.Cryptography;

namespace Pos.SharedKernel.Ids;

/// <summary>
/// INVARIANT #1 — edge-generated, globally-unique, time-ordered identifiers.
/// Generates UUIDv7 (RFC 9562): a 48-bit millisecond timestamp followed by random bits,
/// so the values are unique across every till and branch WITHOUT a central sequence, and
/// remain k-sortable so they index well and replay in order during multi-branch sync.
/// On .NET 9+ the body can be replaced with the built-in Guid.CreateVersion7().
/// </summary>
public static class Uuid7
{
    public static Guid NewGuid()
    {
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);

        long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        b[0] = (byte)(ms >> 40);
        b[1] = (byte)(ms >> 32);
        b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16);
        b[4] = (byte)(ms >> 8);
        b[5] = (byte)ms;
        b[6] = (byte)((b[6] & 0x0F) | 0x70); // version 7
        b[8] = (byte)((b[8] & 0x3F) | 0x80); // variant 10xx

        // Reorder so System.Guid's little-endian constructor yields a canonical,
        // time-sortable string identical to the big-endian UUIDv7 above.
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
        return new Guid(b);
    }
}
