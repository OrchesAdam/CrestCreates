namespace CrestCreates.Event.Abstractions;

/// <summary>Delivery semantic only. Consumer-side dedup is <c>RequiresIdempotency</c> on the descriptor.</summary>
public enum EventReliability { BestEffort, AtLeastOnce }
