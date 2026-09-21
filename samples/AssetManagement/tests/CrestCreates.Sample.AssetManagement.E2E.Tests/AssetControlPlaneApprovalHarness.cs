using System.Globalization;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
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
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.ContextPack;
using CrestCreates.Metadata.DescriptorImpact;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Schema;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using DescriptorDraftModel = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Test-only owner of the real Asset approval boundary. It retains the actual
/// package request/output for each audited preview and only exposes checked
/// descriptor definitions after the authoritative request is Activated.
/// </summary>
public sealed class AssetControlPlaneApprovalHarness : IAsyncDisposable
{
    private const string CorrelationId = "asset-approved-host-correlation";

    private readonly ServiceProvider _services;
    private readonly IReadOnlyList<IHostedService> _hostedServices;
    private readonly MutableAssetDescriptorCatalog _catalog;
    private readonly GlobalDescriptorRegistry _globalRegistry;
    private readonly CapturingPackageBuilder _packageBuilder;
    private readonly InMemoryAgentToolInvocationAuditor _auditor;
    private readonly InMemoryActivationBindingArtifactResolver _artifactResolver;
    private readonly IRuntimeStateContractRegistry _state;
    private readonly object _ownerToken = new();
    private readonly IDescriptorStableHashBuilder _stableHashBuilder =
        new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer());

    private AssetControlPlaneApprovalHarness(
        string tenantId,
        string authorId,
        ServiceProvider services,
        IReadOnlyList<IHostedService> hostedServices,
        MutableAssetDescriptorCatalog catalog,
        GlobalDescriptorRegistry globalRegistry,
        CapturingPackageBuilder packageBuilder,
        InMemoryAgentToolInvocationAuditor auditor,
        InMemoryActivationBindingArtifactResolver artifactResolver,
        IRuntimeStateContractRegistry state)
    {
        TenantId = tenantId;
        AuthorId = authorId;
        _services = services;
        _hostedServices = hostedServices;
        _catalog = catalog;
        _globalRegistry = globalRegistry;
        _packageBuilder = packageBuilder;
        _auditor = auditor;
        _artifactResolver = artifactResolver;
        _state = state;
    }

    public string TenantId { get; }
    public string AuthorId { get; }
    public IServiceProvider Services => _services;

    public static async Task<AssetControlPlaneApprovalHarness> CreateAsync(
        string tenantId = "asset-approved-host-tenant",
        string authorId = "asset-author-agent",
        Action<IServiceCollection>? configureServices = null)
    {
        var baseline = BuildDeployedBaseline();
        CurrentDescriptorDependencyGraph? currentGraph = null;
        var catalog = new MutableAssetDescriptorCatalog(
            baseline,
            () => currentGraph
                ?? throw new InvalidOperationException("The current descriptor dependency graph is not initialized."));
        var globalRegistry = new GlobalDescriptorRegistry();
        foreach (var descriptor in baseline)
            globalRegistry.Register(descriptor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
        services.AddSingleton<IActivationBindingArtifactResolver, InMemoryActivationBindingArtifactResolver>();
        services.AddSingleton<IDescriptorActivationAuditor, InMemoryDescriptorActivationAuditor>();
        services.AddSingleton<IRuntimeActivationGate, InMemoryRuntimeActivationGate>();
        services.AddSingleton<IGlobalDescriptorRegistry>(globalRegistry);
        services.AddSingleton<IDescriptorCatalog>(catalog);
        services.AddSingleton<IDescriptorDependencyGraph>(sp =>
            currentGraph ??= new CurrentDescriptorDependencyGraph(
                sp.GetRequiredService<IDescriptorTopologyBuilder>(), catalog));
        services.AddSingleton<IDescriptorActivationPolicyProvider, RequireHumanReviewPolicyProvider>();
        services.AddSingleton<IHumanTaskRegistry>(sp =>
            CreateHumanTaskRegistry(sp.GetRequiredService<IRegistryValidationEngine<HumanTaskDescriptor>>()));

        services.AddDescriptorDrafts();
        services.AddDescriptorPackaging();
        services.RemoveAll<IDescriptorPackageBuilder>();
        services.AddSingleton<DefaultDescriptorPackageBuilder>();
        services.AddSingleton<IDescriptorPackageBuilder>(sp =>
            new CapturingPackageBuilder(sp.GetRequiredService<DefaultDescriptorPackageBuilder>()));
        services.AddRelationshipKernel();
        services.AddTopologyKernel();
        services.AddDescriptorImpactAnalysis();
        services.AddDescriptorCompatibilityAnalysis();
        services.AddDescriptorLifecycleGovernance();
        services.AddMetadataContextPack();
        services.AddFormKernel();
        services.AddSchemaKernel();
        services.AddRuntimePersistence();
        services.AddCrestCreatesInMemoryRuntimePersistence();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddHumanTaskRuntime();
        services.AddAgentControlPlane(AgentToolAuthorizationOptions.DevelopmentDefaults);
        services.AddSingleton<DefaultActivationReviewOrchestrator>();
        services.AddSingleton<IActivationReviewOrchestrator>(sp =>
            new CapturingActivationReviewOrchestrator(
                sp.GetRequiredService<DefaultActivationReviewOrchestrator>()));
        services.AddRuntimeDelivery(options => options.PollingInterval = TimeSpan.FromMilliseconds(10));
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        return new AssetControlPlaneApprovalHarness(
            tenantId,
            authorId,
            provider,
            hostedServices,
            catalog,
            globalRegistry,
            provider.GetRequiredService<IDescriptorPackageBuilder>() as CapturingPackageBuilder
                ?? throw new InvalidOperationException("Capturing package builder was not registered."),
            provider.GetRequiredService<IAgentToolInvocationAuditor>() as InMemoryAgentToolInvocationAuditor
                ?? throw new InvalidOperationException("In-memory auditor was not registered."),
            provider.GetRequiredService<IActivationBindingArtifactResolver>() as InMemoryActivationBindingArtifactResolver
                ?? throw new InvalidOperationException("In-memory artifact resolver was not registered."),
            provider.GetRequiredService<IRuntimeStateContractRegistry>());
    }

    public async Task<DescriptorDraftModel> ParseAndSaveDraftAsync(
        string authoringJson,
        string intentText,
        string promptInputHash,
        DateTimeOffset? createdAt = null,
        CancellationToken ct = default)
    {
        var parsed = new JsonDescriptorAuthoringOutputParser().Parse(authoringJson, new DescriptorAuthoringParseContext
        {
            TenantId = TenantId,
            AuthorId = AuthorId,
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            IntentText = intentText,
            ExpectedPromptInputHash = promptInputHash
        });
        if (parsed.Status is not (DescriptorAuthoringStatus.Succeeded or DescriptorAuthoringStatus.SucceededWithDiagnostics))
            throw new InvalidOperationException(
                $"Asset authoring parse failed: {string.Join(" | ", parsed.Diagnostics.Select(d => d.Message))}");

        var draft = parsed.DraftSet.Drafts.Should().ContainSingle().Which;
        await _services.GetRequiredService<IDescriptorDraftStore>().SaveAsync(draft, ct);
        return draft;
    }

    public async Task<ApprovalSubmission> SubmitAsync(
        DescriptorDraftModel draft,
        CancellationToken ct = default)
    {
        if (!int.TryParse(draft.ProposedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var draftVersion)
            || draftVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Draft '{draft.DraftId}' has invalid ProposedVersion '{draft.ProposedVersion}'. Expected a positive invariant-culture integer.");
        }

        var service = _services.GetRequiredService<IAgentControlPlaneToolService>();
        var review = await service.ReviewDescriptorDraftAsync(
            Context(AgentToolName.ReviewDescriptorDraft), draft.DraftId, ct);
        EnsureSuccess(review, "ReviewDescriptorDraft");
        var reviewValue = review.Value ?? throw new InvalidOperationException("Review result was empty.");
        var reviewResultId = LastAudit(AgentToolName.ReviewDescriptorDraft)
            .TouchedReviewResultIds!.Single();

        var captureStart = _packageBuilder.Count;
        var preview = await service.PreviewDescriptorPackageAsync(
            Context(AgentToolName.PreviewDescriptorPackage), draft.DraftId, ct);
        EnsureSuccess(preview, "PreviewDescriptorPackage");
        var packagePreviewId = LastAudit(AgentToolName.PreviewDescriptorPackage)
            .TouchedPackagePreviewIds!.Single();
        _packageBuilder.BindPreview(TenantId, packagePreviewId, captureStart);

        var evidence = await service.BuildPackageEvidencePreviewAsync(
            Context(AgentToolName.BuildPackageEvidencePreview), draft.DraftId, ct);
        EnsureSuccess(evidence, "BuildPackageEvidencePreview");
        var evidencePreviewId = LastAudit(AgentToolName.BuildPackageEvidencePreview)
            .TouchedPackagePreviewIds!.Last();

        var resolved = await _artifactResolver.ResolveAsync(TenantId, new ActivationBindingSnapshot
        {
            TenantId = TenantId,
            DraftId = draft.DraftId,
            DraftVersion = draftVersion,
            ReviewResultId = reviewResultId,
            PackagePreviewId = packagePreviewId,
            EvidencePreviewId = evidencePreviewId,
            Hashes = null!,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        var packageHashes = resolved.CurrentPackageHashes
            ?? throw new InvalidOperationException("Package hashes were not retained.");
        var sourceReviewHash = resolved.CurrentSourceReviewHash
            ?? throw new InvalidOperationException("Source review hash was not retained.");
        var reviewManifestHash = resolved.CurrentReviewManifestHash
            ?? throw new InvalidOperationException("Review manifest hash was not retained.");

        var submit = await service.SubmitActivationRequestAsync(
            Context(AgentToolName.SubmitActivationRequest), new SubmitActivationRequestRequest
            {
                DraftId = draft.DraftId,
                BindingSnapshot = new ActivationBindingSnapshot
                {
                    TenantId = TenantId,
                    DraftId = draft.DraftId,
                    DraftVersion = draftVersion,
                    ReviewResultId = reviewResultId,
                    PackagePreviewId = packagePreviewId,
                    EvidencePreviewId = evidencePreviewId,
                    Hashes = new BindingHashes
                    {
                        SourceReviewHash = sourceReviewHash,
                        ReviewManifestHash = reviewManifestHash,
                        PackageManifestHash = packageHashes.PackageManifestHash,
                        PackageEvidenceHash = packageHashes.PackageEvidenceHash,
                        PackageEvidenceEnvelopeHash = packageHashes.PackageEvidenceEnvelopeHash,
                        ContractHash = reviewValue.StableHashes!.ContractHash,
                        DefinitionHash = reviewValue.StableHashes.DefinitionHash
                    },
                    CreatedAt = DateTimeOffset.UtcNow
                }
            }, ct);
        EnsureSuccess(submit, "SubmitActivationRequest");
        var request = submit.Value ?? throw new InvalidOperationException("Activation request was empty.");
        var orchestrator = _services.GetRequiredService<IActivationReviewOrchestrator>() as CapturingActivationReviewOrchestrator
            ?? throw new InvalidOperationException("Capturing review orchestrator was not registered.");
        var taskId = orchestrator.LastTaskId ?? throw new InvalidOperationException("Review task id was empty.");
        var task = await _services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetAsync(new RuntimeInstanceKey(TenantId, taskId), ct)
            ?? throw new InvalidOperationException("Activation review HumanTask was not persisted.");

        return new ApprovalSubmission(draft, request, task, packagePreviewId, evidencePreviewId);
    }

    public async Task<ActivationRequest> CompleteHumanAsync(
        ApprovalSubmission submission,
        string actorId,
        string outcome,
        IReadOnlyList<string>? actorRoles = null,
        CancellationToken ct = default)
    {
        var taskInput = _state.Restore<DescriptorActivationReviewTaskInput>(submission.Task.Input!);
        var decision = new DescriptorActivationReviewDecision
        {
            ActivationRequestId = submission.Request.RequestId,
            TenantId = taskInput.TenantId,
            CorrelationId = taskInput.CorrelationId ?? string.Empty,
            Decision = outcome switch
            {
                "Approve" => DescriptorActivationReviewOutcome.Approved,
                "Reject" => DescriptorActivationReviewOutcome.Rejected,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Outcome must be Approve or Reject.")
            },
            ActorKind = DescriptorActivationActorKind.Human,
            ActorId = actorId,
            Reason = $"Asset human review: {outcome}.",
            DecidedAt = DateTimeOffset.UtcNow,
            BoundEvidenceHash = taskInput.BoundHashes!.PackageEvidenceHash,
            BoundEnvelopeHash = taskInput.BoundHashes.PackageEvidenceEnvelopeHash
        };

        using (var scope = _services.CreateScope())
        {
            var completed = await scope.ServiceProvider.GetRequiredService<IHumanTaskRuntime>().CompleteAsync(
                new HumanTaskCompletionRequest
                {
                    HumanTaskKey = submission.Task.Key,
                    Outcome = outcome,
                    ActorId = actorId,
                    ActorRoles = (actorRoles ?? ["AssetReviewer"]).ToArray(),
                    Result = _state.Capture(decision)
                }, ct);
            if (completed.Status != HumanTaskInstanceStatus.Completed)
                throw new InvalidOperationException("The human review task did not complete.");
        }

        var expected = decision.Decision == DescriptorActivationReviewOutcome.Approved
            ? ActivationRequestStatus.Activated
            : ActivationRequestStatus.Rejected;
        return await WaitForStatusAsync(submission.Request.RequestId, expected, ct);
    }

    public async Task<CheckedApprovedInventory> CheckApprovedInventoryAsync(
        ActivationRequest submittedRequest,
        CancellationToken ct = default)
    {
        var service = _services.GetRequiredService<IAgentControlPlaneToolService>();
        var status = await service.GetActivationRequestStatusAsync(
            Context(AgentToolName.GetActivationRequestStatus), submittedRequest.RequestId, ct);
        EnsureSuccess(status, "GetActivationRequestStatus");
        var request = status.Value ?? throw new InvalidOperationException("Authoritative request was empty.");
        if (request.Status != ActivationRequestStatus.Activated)
            throw new InvalidOperationException($"Only Activated requests may load approved content; actual status was {request.Status}.");

        var previewId = request.BindingSnapshot.PackagePreviewId;
        var preview = await service.GetPackagePreviewAsync(
            Context(AgentToolName.GetPackagePreview), previewId, ct);
        EnsureSuccess(preview, "GetPackagePreview");
        var previewValue = preview.Value ?? throw new InvalidOperationException("Package preview was empty.");
        if (previewValue.PackageManifestHash is null
            || previewValue.PackageEvidenceHash is null
            || previewValue.PackageEvidenceEnvelopeHash is null
            || !_packageBuilder.TryGet(TenantId, previewId, out var capture))
        {
            throw new InvalidOperationException("The authoritative preview did not have retained package content.");
        }

        var rebuilt = _packageBuilder.Rebuild(capture);
        var rebuiltHashes = rebuilt.Hashes ?? throw new InvalidOperationException("Retained package could not be rebuilt.");
        var boundHashes = request.BindingSnapshot.Hashes;
        if (!HashEquals(rebuiltHashes.PackageManifestHash, boundHashes.PackageManifestHash)
            || !HashEquals(rebuiltHashes.PackageEvidenceHash, boundHashes.PackageEvidenceHash)
            || !HashEquals(rebuiltHashes.PackageEvidenceEnvelopeHash, boundHashes.PackageEvidenceEnvelopeHash)
            || !HashEquals(previewValue.PackageManifestHash, boundHashes.PackageManifestHash)
            || !HashEquals(previewValue.PackageEvidenceHash, boundHashes.PackageEvidenceHash)
            || !HashEquals(previewValue.PackageEvidenceEnvelopeHash, boundHashes.PackageEvidenceEnvelopeHash))
        {
            throw new InvalidOperationException("Approved package canonical hashes did not match the authoritative binding.");
        }

        return new CheckedApprovedInventory(
            _ownerToken,
            TenantId,
            request,
            capture.Request.Descriptors,
            capture.Package,
            previewId);
    }

    public async Task AddCheckedDescriptorsToCatalogAsync(
        CheckedApprovedInventory checkedInventory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkedInventory);
        if (!ReferenceEquals(checkedInventory.OwnerToken, _ownerToken)
            || !string.Equals(checkedInventory.TenantId, TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Only content checked from an Activated request may enter the fixture catalog.");

        var fresh = await CheckApprovedInventoryAsync(checkedInventory.Request, ct);
        if (!DescriptorCollectionsMatch(checkedInventory.Descriptors, fresh.Descriptors))
        {
            throw new InvalidOperationException(
                "The checked inventory receipt no longer matches the authoritative retained package content.");
        }

        EnsureGlobalRegistryCompatibility(fresh.Descriptors);
        _catalog.AddRange(fresh.Descriptors);
        RegisterGlobalDistinct(fresh.Descriptors);
    }

    public async ValueTask DisposeAsync()
    {
        for (var index = _hostedServices.Count - 1; index >= 0; index--)
            await _hostedServices[index].StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
    }

    private async Task<ActivationRequest> WaitForStatusAsync(
        string requestId,
        ActivationRequestStatus expected,
        CancellationToken ct)
    {
        var service = _services.GetRequiredService<IAgentControlPlaneToolService>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        AgentToolResult<ActivationRequest>? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await service.GetActivationRequestStatusAsync(
                Context(AgentToolName.GetActivationRequestStatus), requestId, ct);
            if (last.Value?.Status == expected)
                return last.Value;
            await Task.Delay(20, ct);
        }

        var requestDiagnostics = last?.Value?.Diagnostics ?? Array.Empty<AgentToolDiagnostic>();
        var statusDiagnostics = last?.Diagnostics ?? Array.Empty<AgentToolDiagnostic>();
        var diagnostics = requestDiagnostics
            .Concat(statusDiagnostics)
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .ToArray();
        var diagnosticsText = diagnostics.Length == 0
            ? "<none>"
            : string.Join(" | ", diagnostics);

        throw new TimeoutException(
            $"Activation request '{requestId}' did not reach '{expected}'. Actual status: {last?.Value?.Status.ToString() ?? "<no response>"}. Request diagnostics/stale reason: {diagnosticsText}.");
    }

    private AgentToolInvocationContext Context(string toolName) => new()
    {
        TenantId = TenantId,
        ActorId = AuthorId,
        ActorKind = AgentToolActorKind.Agent,
        CorrelationId = CorrelationId,
        ToolName = toolName,
        InvocationSource = AgentToolInvocationSource.Direct
    };

    private AgentToolInvocationAuditRecord LastAudit(string toolName)
        => _auditor.GetAllRecords()
            .Where(record => record.Context.ToolName == toolName)
            .OrderByDescending(record => record.Timestamp)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No audit record was recorded for '{toolName}'.");

    private static void EnsureSuccess<T>(AgentToolResult<T> result, string operation)
        where T : class
    {
        if (result.Status != AgentToolResultStatus.Success)
            throw new InvalidOperationException(
                $"{operation} failed: {string.Join(" | ", result.Diagnostics.Select(d => d.Message))}");
    }

    private static bool HashEquals(CanonicalHash? left, CanonicalHash? right)
        => left is not null && right is not null && left.Equals(right);

    private bool DescriptorCollectionsMatch(
        IReadOnlyList<IDescriptor> expected,
        IReadOnlyList<IDescriptor> actual)
    {
        if (expected.Count != actual.Count)
            return false;

        var expectedByKey = expected.ToDictionary(DescriptorKey.Create);
        var actualByKey = actual.ToDictionary(DescriptorKey.Create);
        if (expectedByKey.Count != actualByKey.Count)
            return false;

        foreach (var (key, expectedDescriptor) in expectedByKey)
        {
            if (!actualByKey.TryGetValue(key, out var actualDescriptor)
                || !_stableHashBuilder.Build(expectedDescriptor).Equals(
                    _stableHashBuilder.Build(actualDescriptor)))
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureGlobalRegistryCompatibility(IEnumerable<IDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            var existing = _globalRegistry.GetByKind(descriptor.Kind)
                .FirstOrDefault(candidate => DescriptorKey.Create(candidate).Equals(DescriptorKey.Create(descriptor)));
            if (existing is not null
                && !_stableHashBuilder.Build(existing).Equals(_stableHashBuilder.Build(descriptor)))
            {
                throw new InvalidOperationException(
                    $"Descriptor {descriptor.FullId} v{(descriptor as IVersionedDescriptor)?.Version} " +
                    "already exists in the global registry with different canonical hashes.");
            }
        }
    }

    private void RegisterGlobalDistinct(IEnumerable<IDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            var key = DescriptorKey.Create(descriptor);
            if (_globalRegistry.GetByKind(descriptor.Kind)
                .Any(candidate => DescriptorKey.Create(candidate).Equals(key)))
            {
                continue;
            }

            _globalRegistry.Register(descriptor);
        }
    }

    public static IReadOnlyList<IDescriptor> BuildDeployedBaseline()
        => AssetDescriptorCatalog.Schemas.Cast<IDescriptor>()
            .Concat(AssetDescriptorCatalog.Capabilities)
            .Append(AssetDescriptorCatalog.MaintenanceForm)
            .Append(AssetDescriptorCatalog.MaintenanceHumanTask)
            .Append(AssetDescriptorCatalog.MaintenanceWorkflow)
            .ToArray();

    private static IHumanTaskRegistry CreateHumanTaskRegistry(
        IRegistryValidationEngine<HumanTaskDescriptor> validationEngine)
    {
        var registry = new HumanTaskRegistry(validationEngine);
        registry.Build(
        [
            new SingleDescriptorProvider<HumanTaskDescriptor>(new HumanTaskDescriptor
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
            })
        ]);
        return registry;
    }

    private sealed class SingleDescriptorProvider<T>(T descriptor) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => [descriptor];
    }

    private sealed class RequireHumanReviewPolicyProvider : IDescriptorActivationPolicyProvider
    {
        public Task<DescriptorActivationPolicy> GetPolicyAsync(
            string tenantId, DescriptorKind? descriptorKind = null, CancellationToken ct = default)
            => Task.FromResult(new DescriptorActivationPolicy
            {
                RequireHumanReviewForAll = true,
                ForbidSelfApproval = true,
                AutoActivateAllowedWhenPolicyPermits = true
            });
    }

    private sealed class CapturingActivationReviewOrchestrator(
        DefaultActivationReviewOrchestrator inner) : IActivationReviewOrchestrator
    {
        public string? LastTaskId { get; private set; }

        public async Task<AgentToolResult<string>> CreateActivationReviewTaskAsync(
            AgentToolInvocationContext context,
            ActivationRequest activationRequest,
            DescriptorActivationPolicy policy,
            CancellationToken ct = default)
        {
            var result = await inner.CreateActivationReviewTaskAsync(context, activationRequest, policy, ct);
            LastTaskId = result.Value;
            return result;
        }

        public Task<ActivationReviewDispatchOutcome> ProcessReviewDecisionAsync(
            DescriptorActivationReviewDecision reviewDecision,
            string completionEventId,
            CancellationToken ct = default)
            => inner.ProcessReviewDecisionAsync(reviewDecision, completionEventId, ct);
    }

    private sealed class CapturingPackageBuilder(IDescriptorPackageBuilder inner) : IDescriptorPackageBuilder
    {
        private readonly List<CapturedPackage> _captures = [];

        public int Count => _captures.Count;

        public DescriptorPackage Build(DescriptorPackageBuildRequest request)
        {
            var package = inner.Build(request);
            _captures.Add(new CapturedPackage(request, package));
            return package;
        }

        public void BindPreview(string tenantId, string previewId, int captureStart)
        {
            if (captureStart < 0 || captureStart > _captures.Count)
                throw new ArgumentOutOfRangeException(nameof(captureStart));

            var candidates = _captures
                .Skip(captureStart)
                .Where(capture => capture.TenantId is null)
                .ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException(
                    $"Expected exactly one package build from this serial Preview call, observed {candidates.Length}.");

            var index = _captures.IndexOf(candidates[0]);
            _captures[index] = candidates[0] with { TenantId = tenantId, PreviewId = previewId };
        }

        public bool TryGet(string tenantId, string previewId, out CapturedPackage capture)
        {
            capture = _captures.LastOrDefault(item =>
                string.Equals(item.TenantId, tenantId, StringComparison.Ordinal)
                && string.Equals(item.PreviewId, previewId, StringComparison.Ordinal))!;
            return capture is not null;
        }

        public DescriptorPackage Rebuild(CapturedPackage capture) => inner.Build(capture.Request);

        public sealed record CapturedPackage(
            DescriptorPackageBuildRequest Request,
            DescriptorPackage Package,
            string? TenantId = null,
            string? PreviewId = null);
    }

    private readonly record struct DescriptorKey(
        string Namespace,
        string Id,
        int? Version,
        DescriptorKind Kind)
    {
        public static DescriptorKey Create(IDescriptor descriptor)
            => new(
                descriptor.Namespace,
                descriptor.Id,
                (descriptor as IVersionedDescriptor)?.Version,
                descriptor.Kind);
    }

    private sealed class CurrentDescriptorDependencyGraph(
        IDescriptorTopologyBuilder topologyBuilder,
        MutableAssetDescriptorCatalog catalog) : IDescriptorDependencyGraph
    {
        private DescriptorDependencyGraphAdapter Snapshot()
            => new(topologyBuilder, catalog.Snapshot());

        public IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId)
            => Snapshot().GetDependencies(descriptorId);

        public IReadOnlyList<DependencyEdge> GetDependents(string descriptorId)
            => Snapshot().GetDependents(descriptorId);

        public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion)
            => Snapshot().AnalyzeImpact(descriptorId, fromVersion, toVersion);

        public void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
            => Snapshot().AddEdge(sourceId, targetId, kind);
    }

    private sealed class MutableAssetDescriptorCatalog(
        IEnumerable<IDescriptor> baseline,
        Func<IDescriptorDependencyGraph> dependencyGraph) : IDescriptorCatalog
    {
        private readonly object _gate = new();
        private readonly List<IDescriptor> _descriptors = baseline.ToList();
        private readonly Func<IDescriptorDependencyGraph> _dependencyGraph = dependencyGraph;

        public IReadOnlyList<IDescriptor> Snapshot()
        {
            lock (_gate)
                return _descriptors.ToArray();
        }

        public void AddRange(IEnumerable<IDescriptor> descriptors)
        {
            lock (_gate)
            {
                foreach (var descriptor in descriptors)
                {
                    var version = (descriptor as IVersionedDescriptor)?.Version;
                    _descriptors.RemoveAll(existing =>
                        existing.Namespace == descriptor.Namespace
                        && existing.Id == descriptor.Id
                        && (existing as IVersionedDescriptor)?.Version == version);
                    _descriptors.Add(descriptor);
                }
            }
        }

        public IDescriptor? Get(string id)
        {
            lock (_gate)
                return _descriptors.LastOrDefault(descriptor => descriptor.Id == id);
        }

        public IEnumerable<IDescriptor> GetAll()
        {
            lock (_gate)
                return _descriptors.ToArray();
        }

        public IEnumerable<IDescriptor> FindByKind(DescriptorKind kind)
        {
            lock (_gate)
                return _descriptors.Where(descriptor => descriptor.Kind == kind).ToArray();
        }

        public IEnumerable<IDescriptor> FindByPackage(string packageId) => Array.Empty<IDescriptor>();
        public IEnumerable<IDescriptor> FindDependents(string descriptorId)
            => _dependencyGraph().GetDependents(descriptorId)
                .Select(edge => Get(edge.SourceId))
                .Where(descriptor => descriptor is not null)!;

        public IEnumerable<IDescriptor> FindDependencies(string descriptorId)
            => _dependencyGraph().GetDependencies(descriptorId)
                .Select(edge => Get(edge.TargetId))
                .Where(descriptor => descriptor is not null)!;

        public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion)
            => _dependencyGraph().AnalyzeImpact(descriptorId, fromVersion, toVersion);
    }

    public sealed record ApprovalSubmission(
        DescriptorDraftModel Draft,
        ActivationRequest Request,
        HumanTaskInstance Task,
        string PackagePreviewId,
        string EvidencePreviewId);

    public sealed class CheckedApprovedInventory
    {
        internal CheckedApprovedInventory(
            object ownerToken,
            string tenantId,
            ActivationRequest request,
            IReadOnlyList<IDescriptor> descriptors,
            DescriptorPackage retainedPackage,
            string packagePreviewId)
        {
            OwnerToken = ownerToken;
            TenantId = tenantId;
            Request = request;
            Descriptors = descriptors.ToArray();
            RetainedPackage = retainedPackage;
            PackagePreviewId = packagePreviewId;
        }

        internal object OwnerToken { get; }
        internal string TenantId { get; }
        public ActivationRequest Request { get; }
        public IReadOnlyList<IDescriptor> Descriptors { get; }
        public DescriptorPackage RetainedPackage { get; }
        public string PackagePreviewId { get; }
    }
}
