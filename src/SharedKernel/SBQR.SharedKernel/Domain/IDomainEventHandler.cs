using MediatR;

namespace SBQR.SharedKernel.Domain;

/// <summary>
/// Marker for handlers of a specific domain event type. Concrete
/// implementations are picked up by MediatR's assembly scan in the host.
/// </summary>
/// <typeparam name="TEvent">Concrete <see cref="IDomainEvent"/> subtype.</typeparam>
/// <remarks>
/// Note: MediatR's INotificationHandler&lt;T&gt; is invariant, so this interface
/// must also be invariant (no <c>in</c> modifier). Domain event handlers
/// therefore cannot be assigned across hierarchies — by design.
/// </remarks>
public interface IDomainEventHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent
{
}
