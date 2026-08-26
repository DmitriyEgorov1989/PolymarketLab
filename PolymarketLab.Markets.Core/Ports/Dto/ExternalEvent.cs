namespace PolymarketLab.Markets.Core.Ports.Dto;

/// <summary>
///     Represents a Polymarket event resolved to the single child market supported by registration.
/// </summary>
/// <param name="ExternalEventId">The Gamma event identifier.</param>
/// <param name="Slug">The Gamma event slug.</param>
/// <param name="Market">The event's only child market.</param>
public sealed record ExternalEvent(
    string ExternalEventId,
    string Slug,
    ExternalMarket Market);
