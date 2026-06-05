using System.Globalization;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Hosting;
using CrestCreates.Localization.Services;

namespace CrestCreates.Localization.Modules;

[CrestModule]
public class LocalizationModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton<ILocalizationService, LocalizationService>();
    }

    public override Task OnApplicationInitializationAsync(IHost host)
    {
        var app = host.Services.GetRequiredService<IApplicationBuilder>();

        var supportedCultures = new[]
        {
            new CultureInfo("en"),
            new CultureInfo("zh-CN"),
            new CultureInfo("zh-TW")
        };

        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture("en"),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures
        });
        return Task.CompletedTask;
    }
}
