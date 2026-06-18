using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Authorization;


[CrestModule]
public class AuthorizationModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddCrestAuthorization();
    }

}
