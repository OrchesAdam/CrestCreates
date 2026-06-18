namespace CrestCreates.Capability;

public sealed class CapabilityPipelineBuilder
{
    private readonly List<Type> _middlewareTypes = new();

    public CapabilityPipelineBuilder Use<TMiddleware>() where TMiddleware : ICapabilityPipelineMiddleware
    {
        _middlewareTypes.Add(typeof(TMiddleware));
        return this;
    }

    public CapabilityPipelineBuilder Clear()
    {
        _middlewareTypes.Clear();
        return this;
    }

    public IReadOnlyList<Type> MiddlewareTypes => _middlewareTypes.AsReadOnly();
}
