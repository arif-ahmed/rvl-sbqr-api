namespace SBQR.SharedKernel.Domain;

/// <summary>
/// Base class for all DDD entities. Identity is the only invariant that
/// distinguishes an entity from another; the rest is value semantics.
/// </summary>
/// <typeparam name="TId">Strongly-typed identifier (e.g. <see cref="Guid"/>, or a typed wrapper).</typeparam>
public abstract class Entity<TId>
    where TId : notnull
{
    protected Entity(TId id)
    {
        if (id is null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        Id = id;
    }

    public TId Id { get; }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
