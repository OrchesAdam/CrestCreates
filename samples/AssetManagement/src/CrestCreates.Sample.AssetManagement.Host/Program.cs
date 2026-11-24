using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.DynamicApi;
using CrestCreates.OpenApi;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

var goldenScenario = args.Contains("--golden-scenario", StringComparer.Ordinal);
var candidateProfile = args.Contains("--golden-scenario-profile=asset-v2-candidate", StringComparer.Ordinal);
if (candidateProfile && !goldenScenario)
    throw new InvalidOperationException("The Asset v2 candidate profile is only valid with --golden-scenario.");
var candidateGoldenScenario = goldenScenario && candidateProfile;
var evolutionScenario = args.Contains("--evolve-asset", StringComparer.Ordinal);
if (evolutionScenario && goldenScenario)
    throw new InvalidOperationException("The production evolution operator cannot be combined with a golden scenario.");
var builder = WebApplication.CreateBuilder(args);
if (goldenScenario)
    builder.WebHost.UseUrls("http://127.0.0.1:0");

// The host module owns composition; this entry point keeps arguments, activation and the pipeline.
var host = new AssetHostModule(builder.Configuration, goldenScenario, candidateGoldenScenario, evolutionScenario);
host.OnConfigureServices(builder.Services);

var app = builder.Build();
await host.OnApplicationInitializationAsync(app);

if (evolutionScenario)
{
    var tenantId = Option("--tenant") ?? throw new InvalidOperationException("--tenant is required for production evolution.");
    var intent = Option("--intent") ?? throw new InvalidOperationException("--intent is required for production evolution.");
    var approverId = Option("--approver") ?? throw new InvalidOperationException("--approver is required for production evolution.");
    var receiptPath = Option("--receipt-out") ?? throw new InvalidOperationException("--receipt-out is required for production evolution.");
    await app.StartAsync();
    try
    {
        var proposal = await AssetProductionEvolutionOperator.ProposeAsync(app.Services, tenantId, intent);
        Console.WriteLine($"Pending activation request: {proposal.RequestId}");
        Console.WriteLine($"Human review task: {proposal.ReviewTaskId}");
        Console.Write($"Type 'APPROVE {approverId}' to approve as {approverId}: ");
        if (!string.Equals(Console.ReadLine(), $"APPROVE {approverId}", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Approval was not provided; no activation or receipt was produced.");
            return 2;
        }

        var receipt = await AssetProductionEvolutionOperator.ApproveAsync(
            app.Services, tenantId, proposal.RequestId, proposal.ReviewTaskId, approverId);
        await File.WriteAllTextAsync(receiptPath, receipt);
        Console.WriteLine($"Asset production evolution receipt: {Path.GetFullPath(receiptPath)}");
    }
    finally
    {
        await app.StopAsync();
    }

    return 0;
}

app.UseAccountabilityHttpTerminalObserver();
app.UseAuthentication();
app.UseAuthorization();
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

string? Option(string name)
{
    var prefix = name + "=";
    var value = args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    if (!string.IsNullOrWhiteSpace(value))
        return value;
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

public partial class Program;
