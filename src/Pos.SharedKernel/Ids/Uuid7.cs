namespace Pos.SharedKernel.Ids;

/// <summary>
/// INVARIANT #1 — edge-generated, globally-unique, time-ordered identifiers (UUIDv7).
/// On net9+ this delegates to the BCL implementation; the wrapper exists so application
/// code expresses intent ("an edge-minted, time-ordered id") instead of a bare Guid call.
/// </summary>
public static class Uuid7
{
    public static Guid NewGuid() => Guid.CreateVersion7();
}
