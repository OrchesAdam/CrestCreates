namespace CrestCreates.Runtime.Delivery.Abstractions.Composition;

public sealed record ActiveOutboxRequirements(IReadOnlySet<string> ContractIds, IReadOnlySet<string> ConsumerIds);
