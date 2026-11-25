using System.Collections.Concurrent;
using CrestCreates.Agent.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Form;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.ContextPack;
using CrestCreates.Metadata.DescriptorImpact;
using CrestCreates.Metadata.Registry;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Schema;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

public static class AssetProductionEvolutionOperator
{
    public const string EnabledSetting = "AssetManagement:Evolution:Enabled";

    public static IServiceCollection AddAssetProductionEvolutionOperator(this IServiceCollection services)
    {
        IDescriptorDependencyGraph? graph = null;
        var baseline = AssetProductionEvolutionBaseline.GetDescriptors();
        var catalog = new OperatorDescriptorCatalog(baseline, () => graph ??
            throw new InvalidOperationException("The Asset evolution dependency graph is not initialized."));
        var globalRegistry = new GlobalDescriptorRegistry();
        foreach (var descriptor in baseline)
            globalRegistry.Register(descriptor);

        services.AddSingleton<IGlobalDescriptorRegistry>(globalRegistry);
        services.AddSingleton<IDescriptorCatalog>(catalog);
        services.AddSingleton<IDescriptorDependencyGraph>(sp => graph ??= new DescriptorDependencyGraphAdapter(
            sp.GetRequiredService<IDescriptorTopologyBuilder>(), baseline));
        services.AddSingleton<IDescriptorActivationPolicyProvider, OperatorHumanReviewPolicy>();
        services.AddDescriptorDrafts();
        services.AddDescriptorPackaging();
        services.AddRelationshipKernel();
        services.AddTopologyKernel();
        services.AddDescriptorImpactAnalysis();
        services.AddDescriptorCompatibilityAnalysis();
        services.AddDescriptorLifecycleGovernance();
        services.AddMetadataContextPack();
        services.AddSchemaKernel();
        services.AddAgentPrompting();
        services.AddAgentMemoryReadRuntime();
        services.AddDescriptorAuthoring();
        services.AddSingleton<IDescriptorAuthoringModelClient, OperatorProductionModelClient>();
        services.Configure<LlmDescriptorAuthoringAgentOptions>(options =>
        {
            options.AuthorId = "asset-evolution-operator";
            options.ProviderProfileRef = new AgentPromptProviderProfileRef("asset-deterministic-operator");
            options.ModelProfile = new DescriptorAuthoringModelProfile
            {
                ProfileName = "asset-production-v3",
                ProviderName = "crestcreates-deterministic",
                ModelName = "asset-production-workflow-v3",
                SupportsJsonMode = true
            };
        });
        services.AddSingleton<IDescriptorAuthoringAgent, LlmDescriptorAuthoringAgent>();
        services.AddAgentControlPlane(AgentToolAuthorizationOptions.DevelopmentDefaults);
        services.AddAgentControlPlaneInMemoryStubs();
        services.AddSingleton(sp => new OperatorActivationReviewOrchestrator(
            ActivatorUtilities.CreateInstance<DefaultActivationReviewOrchestrator>(sp)));
        services.Replace(ServiceDescriptor.Singleton<IActivationReviewOrchestrator>(
            sp => sp.GetRequiredService<OperatorActivationReviewOrchestrator>()));
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch();
        services.Replace(ServiceDescriptor.Singleton<IHumanTaskRegistry>(sp =>
            CreateHumanTaskRegistry(sp.GetRequiredService<IRegistryValidationEngine<HumanTaskDescriptor>>())));
        services.AddScoped<AssetProductionEvolutionService>();
        services.AddSingleton<AssetProductionReleaseIssuer>();
        services.AddScoped<AssetProductionEvolutionOperatorRunner>();
        return services;
    }

    public static async Task<AssetProductionEvolutionOperatorProposal> ProposeAsync(
        IServiceProvider services, string tenantId, string intent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AssetProductionEvolutionOperatorRunner>()
            .ProposeAsync(tenantId, intent, cancellationToken);
    }

    public static async Task<string> ApproveAsync(IServiceProvider services, string tenantId, string requestId,
        string reviewTaskId, string approverId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewTaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverId);
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AssetProductionEvolutionOperatorRunner>()
            .ApproveAsync(tenantId, requestId, reviewTaskId, approverId, cancellationToken);
    }

    private static IHumanTaskRegistry CreateHumanTaskRegistry(IRegistryValidationEngine<HumanTaskDescriptor> engine)
    {
        var registry = new HumanTaskRegistry(engine);
        registry.Build(
        [
            new AssetDescriptorProvider<HumanTaskDescriptor>([new HumanTaskDescriptor
            {
                Id = DescriptorActivationHumanTaskIds.ActivationReview.RequireValue(),
                Name = "Descriptor activation review",
                Version = 1,
                Interaction = AssetDescriptorCatalog.MaintenanceHumanTask.Interaction,
                AssigneeStrategy = AssigneeStrategy.CandidateGroup,
                State = DescriptorState.Active,
                Outcomes =
                [
                    new CompletionOutcome { Condition = CompletionCondition.Approve },
                    new CompletionOutcome { Condition = CompletionCondition.Reject }
                ]
            }])
        ]);
        return registry;
    }

    private sealed class OperatorHumanReviewPolicy : IDescriptorActivationPolicyProvider
    {
        public Task<DescriptorActivationPolicy> GetPolicyAsync(string tenantId, DescriptorKind? descriptorKind = null,
            CancellationToken ct = default) => Task.FromResult(new DescriptorActivationPolicy
        {
            RequireHumanReviewForAll = true,
            ForbidSelfApproval = true,
            AutoActivateAllowedWhenPolicyPermits = true
        });
    }

    private sealed class OperatorDescriptorCatalog(IEnumerable<IDescriptor> baseline,
        Func<IDescriptorDependencyGraph> dependencyGraph) : IDescriptorCatalog
    {
        private readonly object _gate = new();
        private readonly List<IDescriptor> _descriptors = baseline.ToList();

        public IReadOnlyList<IDescriptor> Snapshot()
        {
            lock (_gate)
                return _descriptors.ToArray();
        }

        public IEnumerable<IDescriptor> GetAll()
        {
            lock (_gate)
                return _descriptors.ToArray();
        }

        public IDescriptor? Get(string id)
        {
            lock (_gate)
                return _descriptors.LastOrDefault(descriptor => descriptor.Id == id);
        }

        public IEnumerable<IDescriptor> FindByKind(DescriptorKind kind)
        {
            lock (_gate)
                return _descriptors.Where(descriptor => descriptor.Kind == kind).ToArray();
        }

        public IEnumerable<IDescriptor> FindByPackage(string packageId) => Array.Empty<IDescriptor>();
        public IEnumerable<IDescriptor> FindDependents(string descriptorId) =>
            dependencyGraph().GetDependents(descriptorId).Select(edge => Get(edge.SourceId)).OfType<IDescriptor>();
        public IEnumerable<IDescriptor> FindDependencies(string descriptorId) =>
            dependencyGraph().GetDependencies(descriptorId).Select(edge => Get(edge.TargetId)).OfType<IDescriptor>();
        public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion) =>
            dependencyGraph().AnalyzeImpact(descriptorId, fromVersion, toVersion);
    }

    private sealed class OperatorProductionModelClient : IDescriptorAuthoringModelClient
    {
        public Task<DescriptorAuthoringModelResponse> CompleteAsync(DescriptorAuthoringModelRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.Prompt.UserPrompt.Contains("critical equipment", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The operator proposal is limited to critical-equipment maintenance evolution.");

            var hash = request.Prompt.PromptInputHash;
            var steps = string.Join(',',
                Step(AssetProductionDescriptors.InitialStepId, AssetDescriptorCatalog.MaintenanceInitialHumanTask.Id, null),
                Step(AssetProductionDescriptors.TrialStepId, AssetProductionDescriptors.TrialTaskId, WorkflowConditionTokens.PreviousHumanTaskApproved),
                Step(AssetProductionDescriptors.QualityStepId, AssetProductionDescriptors.QualityTaskId, WorkflowConditionTokens.AllPriorHumanTasksApproved));
            var response = $$"""
                {
                  "contractVersion":"8a.v1",
                  "outcome":"drafts",
                  "promptInputHash":"{{hash.Value}}",
                  "plan":{
                    "planId":"asset-production-workflow-v3",
                    "intentText":"Require MES trial evidence and independent quality sign-off for critical equipment.",
                    "plannedDescriptorRefs":[{"namespace":"workflow","id":"{{AssetProductionDescriptors.WorkflowId}}","version":3}]
                  },
                  "items":[{
                    "descriptorKind":"Workflow",
                    "descriptorId":"{{AssetProductionDescriptors.WorkflowId}}",
                    "operation":"Update",
                    "baseVersion":"1",
                    "rationale":"Require supervisor review, MES evidence and independent quality sign-off.",
                    "payload":{
                      "id":"{{AssetProductionDescriptors.WorkflowId}}",
                      "name":"Critical equipment maintenance release",
                      "version":3,
                      "steps":[{{steps}}]
                    }
                  }]
                }
                """;
            return Task.FromResult(new DescriptorAuthoringModelResponse
            {
                ResponseText = response,
                ProviderName = request.ModelProfile.ProviderName,
                ModelName = request.ModelProfile.ModelName,
                PromptInputHash = hash
            });
        }

        private static string Step(string id, string taskId, string? condition) => $$"""
            {
              "id":"{{id}}",
              "name":"{{id}}",
              "condition":{{ConditionJson(condition)}},
              "target":{"kind":"HumanTask","humanTask":{"namespace":"humantask","id":"{{taskId}}","version":1} }
            }
            """;

        private static string ConditionJson(string? condition) =>
            condition is null ? "null" : "\"" + condition + "\"";
    }
}

public sealed class OperatorActivationReviewOrchestrator(IActivationReviewOrchestrator inner) : IActivationReviewOrchestrator
{
    private readonly ConcurrentDictionary<(string TenantId, string RequestId), string> _reviewTasks = new();

    public async Task<AgentToolResult<string>> CreateActivationReviewTaskAsync(
        AgentToolInvocationContext context, ActivationRequest activationRequest,
        DescriptorActivationPolicy policy, CancellationToken ct = default)
    {
        var result = await inner.CreateActivationReviewTaskAsync(context, activationRequest, policy, ct);
        if (result.Status == AgentToolResultStatus.Success && result.Value is { } taskId)
            _reviewTasks[(activationRequest.TenantId, activationRequest.RequestId)] = taskId;
        return result;
    }

    public string GetReviewTaskId(string tenantId, string requestId) =>
        _reviewTasks.TryGetValue((tenantId, requestId), out var taskId)
            ? taskId
            : throw new InvalidOperationException("The production activation review task was not created.");

    public Task<ActivationReviewDispatchOutcome> ProcessReviewDecisionAsync(
        DescriptorActivationReviewDecision reviewDecision, string completionEventId, CancellationToken ct = default) =>
        inner.ProcessReviewDecisionAsync(reviewDecision, completionEventId, ct);
}

public sealed record AssetProductionEvolutionOperatorProposal(string RequestId, string ReviewTaskId);

public sealed class AssetProductionEvolutionOperatorRunner(
    AssetProductionEvolutionService evolution,
    OperatorActivationReviewOrchestrator reviewOrchestrator,
    IHumanTaskRuntime humanTasks,
    IHumanTaskInstanceStore taskStore,
    IRuntimeStateContractRegistry state,
    IAgentControlPlaneToolService controlPlane,
    AssetProductionReleaseIssuer issuer)
{
    public async Task<AssetProductionEvolutionOperatorProposal> ProposeAsync(
        string tenantId, string intent, CancellationToken ct)
    {
        const string authorId = "asset-evolution-operator";
        var context = new AgentToolInvocationContext
        {
            TenantId = tenantId,
            ActorId = authorId,
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = $"asset-evolution-{Guid.NewGuid():N}",
            ToolName = AgentToolName.BuildMetadataContextPack,
            InvocationSource = AgentToolInvocationSource.Direct
        };
        var proposal = await evolution.ProposeAndSubmitAsync(context, intent, ct);
        var request = proposal.ActivationRequest ?? throw new InvalidOperationException("Production evolution did not produce an activation request.");
        var reviewTask = await taskStore.GetAsync(new RuntimeInstanceKey(tenantId,
            reviewOrchestrator.GetReviewTaskId(tenantId, request.RequestId)), ct)
            ?? throw new InvalidOperationException("The production activation review task is unavailable.");
        var reviewTaskId = DescriptorActivationHumanTaskIds.ActivationReview.RequireValue();
        if (reviewTask.HumanTaskPin?.Ref.Id != reviewTaskId ||
            reviewTask.Input is null ||
            state.Restore<DescriptorActivationReviewTaskInput>(reviewTask.Input) is not { ActivationRequestId: var taskRequestId } input ||
            taskRequestId != request.RequestId || input.TenantId != tenantId)
            throw new InvalidOperationException("The production activation review task does not match the submitted request.");
        return new AssetProductionEvolutionOperatorProposal(request.RequestId, reviewTask.Id);
    }

    public async Task<string> ApproveAsync(string tenantId, string requestId, string reviewTaskId,
        string approverId, CancellationToken ct)
    {
        const string authorId = "asset-evolution-operator";
        if (string.Equals(approverId, authorId))
            throw new InvalidOperationException("The production evolution requires a different human approver.");
        var reviewTask = await taskStore.GetAsync(new RuntimeInstanceKey(tenantId, reviewTaskId), ct)
            ?? throw new InvalidOperationException("The production activation review task is unavailable.");
        var expectedTaskId = DescriptorActivationHumanTaskIds.ActivationReview.RequireValue();
        if (reviewTask.HumanTaskPin?.Ref.Id != expectedTaskId || reviewTask.Input is null ||
            state.Restore<DescriptorActivationReviewTaskInput>(reviewTask.Input) is not { ActivationRequestId: var taskRequestId } input ||
            taskRequestId != requestId || input.TenantId != tenantId)
            throw new InvalidOperationException("The production activation review task does not match the submitted request.");
        var context = new AgentToolInvocationContext
        {
            TenantId = tenantId,
            ActorId = approverId,
            ActorKind = AgentToolActorKind.Human,
            CorrelationId = input.CorrelationId ?? string.Empty,
            ToolName = AgentToolName.GetActivationRequestStatus,
            InvocationSource = AgentToolInvocationSource.Direct
        };
        await humanTasks.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = reviewTask.Key,
            Outcome = "Approve",
            ActorId = approverId,
            ActorRoles = ["AssetReviewer"],
            Result = state.Capture(new DescriptorActivationReviewDecision
            {
                ActivationRequestId = requestId,
                TenantId = tenantId,
                CorrelationId = input.CorrelationId ?? string.Empty,
                Decision = DescriptorActivationReviewOutcome.Approved,
                ActorKind = DescriptorActivationActorKind.Human,
                ActorId = approverId,
                Reason = "Explicit human approval through the local operator entry.",
                DecidedAt = DateTimeOffset.UtcNow,
                BoundEvidenceHash = input.BoundHashes!.PackageEvidenceHash,
                BoundEnvelopeHash = input.BoundHashes.PackageEvidenceEnvelopeHash
            })
        }, ct);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var status = await controlPlane.GetActivationRequestStatusAsync(
                context, requestId, ct);
            if (status.Value?.Status == ActivationRequestStatus.Activated)
                return await issuer.IssueAsync(context, requestId, ct);
            if (status.Value?.Status == ActivationRequestStatus.Rejected)
                throw new InvalidOperationException("The production evolution activation was rejected.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }

        throw new InvalidOperationException("The production evolution activation did not complete before the operator deadline.");
    }
}
