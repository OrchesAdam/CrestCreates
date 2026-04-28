using Microsoft.AspNetCore.Routing;

namespace CrestCreates.DynamicApi;

public interface IDynamicApiGeneratedProvider
{
    IReadOnlyCollection<System.Reflection.Assembly> ServiceAssemblies { get; }

    DynamicApiRegistry CreateRegistry(DynamicApiOptions options);

    void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options);
}