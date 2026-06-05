namespace Pos.SharedKernel;

/// <summary>Base for entities with identity-based equality.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }

    protected Entity() { }
    protected Entity(Guid id) => Id = id;

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != default;

    public override int GetHashCode() => Id.GetHashCode();
}
