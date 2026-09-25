using MediatR;

namespace SBQR.SharedKernel.Domain;

/// <summary>
/// Marker interface for domain events. A domain event is something that
/// happened in the past tense that domain experts care about — emitted
/// by aggregates via <see cref="AggregateRoot{TId}.RaiseDomainEvent"/>.
/// </summary>
/// <remarks>
/// Extends <see cref="INotification"/> so handlers can be wired through
/// MediatR's <c>INotificationHandler&lt;T&gt;</c> contract. This keeps the
/// domain-event pipeline identical to the in-process pub/sub that MediatR
/// already provides for plain notifications.
/// </remarks>
public interface IDomainEvent : INotification
{
    /// <summary>
    /// UTC timestamp of when the event occurred (set by the raising aggregate
    /// or by an infrastructure interceptor — never by hand).
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
