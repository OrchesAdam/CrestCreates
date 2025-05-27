using Microsoft.AspNetCore.Routing;

namespace CrestCreates.WebAppExtensions;

public abstract class CrestGroupModule : ICrestGroupModule
{
    public abstract void AddRoutes(IEndpointRouteBuilder app);
}