namespace Pos.Application.Abstractions;

/// <summary>
/// Ambient clock port. Injecting it instead of calling DateTimeOffset.UtcNow keeps handlers
/// deterministic under test and lets the store server stamp facts with a single, monitored
/// notion of "now" (relevant when tills drift before the daily NTP sync).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
