using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.DynamicApi;
using CrestCreates.OpenApi;
using CrestCreates.Sample.Procurement.Host;

var goldenScenario = args.Contains("--golden-scenario", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args);
if (goldenScenario)
    builder.WebHost.UseUrls("http://127.0.0.1:0");

// The host module owns composition; this entry point keeps arguments, activation and the pipeline.
var host = new ProcurementHostModule(goldenScenario);
host.OnConfigureServices(builder.Services);

var app = builder.Build();
await host.OnApplicationInitializationAsync(app);

app.UseAccountabilityHttpTerminalObserver();
app.UseAuthentication();
app.UseAccountabilityHttpOperationScope();
app.MapCrestCapabilityEndpoints();
app.MapCrestOpenApi();

if (goldenScenario)
{
    await app.StartAsync();
    try
    {
        return await ProcurementGoldenScenario.RunAsync(app);
    }
    finally
    {
        await app.StopAsync();
    }
}

await app.RunAsync();
return 0;

public partial class Program;
