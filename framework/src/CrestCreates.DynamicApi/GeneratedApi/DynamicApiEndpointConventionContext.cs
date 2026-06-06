using Microsoft.AspNetCore.Builder;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiEndpointConventionContext
{
    public DynamicApiEndpointConventionContext(
        DynamicApiEndpointDescriptor descriptor,
        RouteHandlerBuilder builder)
    {
        Descriptor = descriptor;
        Builder = builder;
    }

    public DynamicApiEndpointDescriptor Descriptor { get; }

    public RouteHandlerBuilder Builder { get; }
}
