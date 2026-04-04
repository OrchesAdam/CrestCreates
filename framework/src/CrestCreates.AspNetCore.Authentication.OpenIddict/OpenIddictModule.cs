using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict;


[CrestModule]
public class OpenIddictModule : ModuleBase
{
   public override void OnConfigureServices(IServiceCollection services)
   {
      services.AddOpenIddictServer();
   }
}
