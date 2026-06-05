namespace Pos.SharedKernel;

public interface IAuditable
{
    DateTimeOffset CreatedAtUtc { get; }
    Guid? CreatedBy { get; }
    DateTimeOffset? UpdatedAtUtc { get; }
    Guid? UpdatedBy { get; }
}
