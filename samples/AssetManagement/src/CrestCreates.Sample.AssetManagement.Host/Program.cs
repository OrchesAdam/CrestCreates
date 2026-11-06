using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.DynamicApi;
using CrestCreates.OpenApi;
using CrestCreates.Sample.AssetManagement.Host;

var goldenScenario = args.Contains("--golden-scenario", StringComparer.Ordinal);
var candidateProfile = args.Contains("--golden-scenario-profile=asset-v2-candidate", StringComparer.Ordinal);
if (candidateProfile && !goldenScenario)
    throw new InvalidOperationException("The Asset v2 candidate profile is only valid with --golden-scenario.");
var candidateGoldenScenario = goldenScenario && candidateProfile;
var builder = WebApplication.CreateBuilder(args);
if (goldenScenario)
    builder.WebHost.UseUrls("http://127.0.0.1:0");

// The host module owns composition; this entry point keeps arguments, activation and the pipeline.
var host = new AssetHostModule(builder.Configuration, goldenScenario, candidateGoldenScenario);
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
    try { return candidateGoldenScenario
        ? await AssetGoldenScenario.RunCandidateV2Async(app)
        : await AssetGoldenScenario.RunAsync(app); }
    finally { await app.StopAsync(); }
}

await app.RunAsync();
return 0;

public partial class Program;
