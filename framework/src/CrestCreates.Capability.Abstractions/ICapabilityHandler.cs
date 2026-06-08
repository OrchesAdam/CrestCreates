namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityHandler
{
}

public interface ICapabilityHandler<TInput, TOutput> : ICapabilityHandler
{
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken ct);
}
