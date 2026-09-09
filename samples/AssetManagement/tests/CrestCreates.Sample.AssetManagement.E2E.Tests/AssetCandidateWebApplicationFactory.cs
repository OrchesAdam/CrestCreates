using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.HumanTask;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Sample.AssetManagement.Application.Handlers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

#pragma warning disable CC1001
/// <summary>
/// Test-only candidate composition. The application keeps its default v1
/// registry; this factory explicitly replaces only the versioned descriptor
/// registries so the business acceptance cases can exercise a v2 candidate.
/// </summary>
public sealed class AssetCandidateWebApplicationFactory : WebApplicationFactory<Program>
{
    public InitialHumanTaskCompletionGate CompletionGate { get; } = new();
    public bool GateInitialCompletion { get; set; }
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"crest-assets-e2e-{Guid.NewGuid():N}.db");
    public string RuntimeSchema { get; } = $"crest_asset_runtime_e2e_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = Environment.GetEnvironmentVariable("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING")
            ?? throw new InvalidOperationException("ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING must point at the durable PostgreSQL test service.");
        var hostDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        builder.UseContentRoot(hostDir);
        builder.UseSetting("AssetManagement:DatabasePath", DatabasePath);
        builder.UseSetting("AssetManagement:RuntimeConnectionString", connectionString);
        builder.UseSetting("AssetManagement:RuntimeSchema", RuntimeSchema);
        builder.ConfigureServices(services =>
        {
            var humanTasks = new HumanTaskRegistry(new RegistryValidationEngine<HumanTaskDescriptor>([]));
            humanTasks.Build([new AssetDescriptorProvider<HumanTaskDescriptor>(
                [AssetDescriptorCatalog.MaintenanceHumanTask, AssetDescriptorCatalog.MaintenanceInitialHumanTask])]);
            services.RemoveAll<IHumanTaskRegistry>();
            services.AddSingleton<IHumanTaskRegistry>(humanTasks);

            services.RemoveAll<ICapabilityHandlerRegistry>();
            services.AddSingleton<ICapabilityHandlerRegistry>(new CandidateCapabilityHandlerRegistry());

            var workflows = new WorkflowRegistry(new RegistryValidationEngine<WorkflowDescriptor>([]));
            var candidateWorkflow = new WorkflowDescriptor
            {
                Id = AssetContractIds.MaintenanceWorkflow,
                Name = "Asset maintenance review",
                Version = 2,
                State = DescriptorState.Active,
                Steps =
                [
                    new WorkflowStep
                    {
                        Id = "maintenance-initial-review",
                        Name = "Manager initial maintenance review",
                        Target = new HumanTaskTarget
                        {
                            HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(AssetContractIds.MaintenanceInitialHumanTask, 1)
                        }
                    },
                    new WorkflowStep
                    {
                        Id = "maintenance-final-review",
                        Name = "Manager final maintenance review",
                        Condition = WorkflowConditionTokens.PreviousHumanTaskApproved,
                        Target = new HumanTaskTarget
                        {
                            HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(AssetContractIds.MaintenanceHumanTask, 1)
                        }
                    }
                ]
            };
            workflows.Build([new AssetDescriptorProvider<WorkflowDescriptor>(
                [AssetDescriptorCatalog.MaintenanceWorkflow, candidateWorkflow])]);
            services.RemoveAll<IWorkflowRegistry>();
            services.AddSingleton<IWorkflowRegistry>(workflows);

            services.RemoveAll<IDescriptorLookup>();
            services.AddSingleton<IDescriptorLookup>(new AssetDescriptorLookup(
                AssetDescriptorCatalog.Schemas.Cast<IDescriptor>()
                    .Concat(AssetDescriptorCatalog.Capabilities)
                    .Append(AssetDescriptorCatalog.MaintenanceForm)
                    .Append(AssetDescriptorCatalog.MaintenanceHumanTask)
                    .Append(AssetDescriptorCatalog.MaintenanceInitialHumanTask)
                    .Append(AssetDescriptorCatalog.MaintenanceWorkflow)
                    .Append(candidateWorkflow)));

            if (GateInitialCompletion)
            {
                var runtimeDescriptor = services.LastOrDefault(service => service.ServiceType == typeof(IHumanTaskRuntime));
                if (runtimeDescriptor?.ImplementationType is not { } runtimeType)
                    throw new InvalidOperationException("The test composition could not locate the production HumanTask runtime.");
                services.RemoveAll<IHumanTaskRuntime>();
                services.AddScoped<IHumanTaskRuntime>(sp => new InitialHumanTaskCompletionGateRuntime(
                    (IHumanTaskRuntime)ActivatorUtilities.CreateInstance(sp, runtimeType), CompletionGate));
            }
        });
    }

}

public sealed class InitialHumanTaskCompletionGate
{
    private int _armed = 1;
    public TaskCompletionSource<HumanTaskInstance> Persisted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool TryArm() => Interlocked.Exchange(ref _armed, 0) == 1;
}

internal sealed class InitialHumanTaskCompletionGateRuntime(
    IHumanTaskRuntime inner,
    InitialHumanTaskCompletionGate gate) : IHumanTaskRuntime
{
    public Task<HumanTaskInstance> PrepareAsync(HumanTaskCreationRequest request, CancellationToken ct = default)
        => inner.PrepareAsync(request, ct);

    public Task<HumanTaskInstance> CreateAsync(HumanTaskCreationRequest request, CancellationToken ct = default)
        => inner.CreateAsync(request, ct);

    public Task<HumanTaskInstance> CancelAsync(RuntimeInstanceKey humanTaskKey, string reason, CancellationToken ct = default)
        => inner.CancelAsync(humanTaskKey, reason, ct);

    public async Task<HumanTaskInstance> CompleteAsync(HumanTaskCompletionRequest request, CancellationToken ct = default)
    {
        var completed = await inner.CompleteAsync(request, ct);
        if (!gate.TryArm())
            return completed;
        gate.Persisted.TrySetResult(completed);
        await gate.Release.Task.WaitAsync(ct);
        return completed;
    }
}
#pragma warning restore CC1001

internal sealed class CandidateCapabilityHandlerRegistry : ICapabilityHandlerRegistry
{
    public IReadOnlyDictionary<string, Type> GetHandlerMappings() => new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        [AssetContractIds.RegisterCapability] = typeof(RegisterAssetHandler),
        [AssetContractIds.GetCapability] = typeof(GetAssetHandler),
        [AssetContractIds.QueryCapability] = typeof(QueryAssetsHandler),
        [AssetContractIds.UpdateCapability] = typeof(UpdateAssetHandler),
        [AssetContractIds.AssignCapability] = typeof(AssignAssetHandler),
        [AssetContractIds.ReturnCapability] = typeof(ReturnAssetHandler),
        [AssetContractIds.TransferCapability] = typeof(TransferAssetHandler),
        [AssetContractIds.RequestMaintenanceCapability] = typeof(RequestMaintenanceHandler),
        [AssetContractIds.ApplyMaintenanceCapability] = typeof(ApplyMaintenanceDecisionHandler)
    };
}
