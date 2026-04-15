using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using CrestCreates.Localization.Services;

namespace CrestCreates.Localization.Modules;

public class LocalizationModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton<ILocalizationService, LocalizationService>();
    }

    public override void OnApplicationInitialization(IHost host)
    {
        base.OnApplicationInitialization(host);

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
    }
}
