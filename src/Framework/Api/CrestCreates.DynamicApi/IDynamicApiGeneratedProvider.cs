using Microsoft.AspNetCore.Routing;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Legacy provider interface for AppService-oriented Dynamic API generated endpoints.
/// New Capability Endpoint projection uses its own descriptor provider interface.
/// </summary>
public interface IDynamicApiGeneratedProvider
{
    IReadOnlyCollection<System.Reflection.Assembly> ServiceAssemblies { get; }

    IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors { get; }

    DynamicApiRegistry CreateRegistry(DynamicApiOptions options);

    void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options);
}