using Microsoft.AspNetCore.Routing;

namespace CrestCreates.WebAppExtensions;

public interface ICrestGroupModule
{
    void AddRoutes(IEndpointRouteBuilder app);
}