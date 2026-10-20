namespace CrestCreates.Domain.Shared.Entities.Auditing;

/// <summary>
/// Optional update-input contract carrying the concurrency stamp the client read. Opt-in services
/// compare it with the stored entity stamp before mapping; an absent/empty value is a precondition
/// failure (428), a mismatch is a concurrency conflict (409).
/// </summary>
public interface IHasConcurrencyStampInput
{
    string ConcurrencyStamp { get; }
}
