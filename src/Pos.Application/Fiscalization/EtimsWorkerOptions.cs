namespace Pos.Application.Fiscalization;

/// <summary>Host-level eTIMS sync-worker tuning (NOT per-tenant). Per-tenant enable/creds live in EtimsSettings.</summary>
public sealed class EtimsWorkerOptions
{
    public int IntervalSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 5;
}
