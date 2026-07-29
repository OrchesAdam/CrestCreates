using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Projection.DescriptorProviders;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Tools;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Agent.Memory.Tools.E2E.Tests;

public sealed partial class AgentMemoryToolPipelineE2ETests
{
    [Fact]
    public async Task Memory_tools_execute_the_generated_pipeline_end_to_end()
    {
        // Force-load Projection assembly so its ModuleInitializer registers shared read schemas
        _ = typeof(AgentMemoryProjectionSchemaProviders).IsPublic;
        var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        var execution = new FixtureExecutionContextAccessor();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddSingleton<ICurrentUser, FixtureCurrentUser>();
        builder.Services.AddSingleton<ITenantContext, FixtureTenantContext>();
        builder.Services.AddSingleton<IPermissionChecker, FixturePermissionChecker>();
        builder.Services.AddSingleton<IAgentExecutionContextAccessor>(execution);
        builder.Services.AddSingleton<IAgentMemoryToolAccessScopeProvider, FixtureScopeProvider>();
        builder.Services.AddSingleton<IAgentMemoryHistoryAccessAuthorizer, FixtureHistoryAuthorizer>();
        builder.Services.AddSingleton<IAgentToolJsonContextContributor, FixtureJsonContributor>();
        builder.Services.AddSingleton<IAgentToolModuleSelection, FixtureModuleSelection>();
        builder.Services.AddSingleton<IAgentToolApprovalGate, FixtureApprovalGate>();
        builder.Services.AddSingleton<IAgentToolInvocationGate, DevelopmentInMemoryAgentToolInvocationGate>();
        builder.Services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp =>
            (IAgentToolInvocationLeaseAbandoner)sp.GetRequiredService<IAgentToolInvocationGate>());
        builder.Services.AddSingleton<IAgentToolBudgetGate, DevelopmentInMemoryAgentToolBudgetGate>();
        builder.Services.AddSingleton<IAgentToolGovernanceAuditor, DevelopmentInMemoryAgentToolGovernanceAuditor>();
        builder.Services.AddAgentMemoryRuntime();
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddAccountability();
        builder.Services.AddCrestAgentTools();
        builder.Services.AddAgentMemoryTools();

        using var host = builder.Build();
        await host.StartAsync();
        var services = host.Services;
        var principal = new AgentMemoryToolPrincipal
        {
            TenantId = "e2e-tenant", UserId = "e2e-user", AgentId = "e2e-agent", ExecutionId = "e2e-execution"
        };
        var conversation = new AgentConversationRecord
        {
            TenantId = principal.TenantId,
            ConversationId = "conversation-e2e",
            Turns =
            [
                new AgentConversationTurn { TurnId = "turn-0", TenantId = principal.TenantId, Role = AgentConversationRole.User, Content = "first turn" },
                new AgentConversationTurn { TurnId = "turn-1", TenantId = principal.TenantId, Role = AgentConversationRole.User, Content = "adjacent turn" }
            ]
        };
        await services.GetRequiredService<IAgentConversationStore>().SaveConversationAsync(conversation);

        var sourceRef = new AgentContextSourceRef
        {
            SourceKind = AgentSourceKind.ConversationTurn, TenantId = principal.TenantId,
            SourceId = conversation.ConversationId, RangeStart = 0, RangeEnd = 0,
            CanonicalContentHash = MemoryHash()
        };
        var memory = new AgentMemoryItem
        {
            MemoryId = "memory-seed", TenantId = principal.TenantId, Kind = AgentMemoryKind.ProjectFact,
            Content = "first turn", CanonicalContentHash = MemoryHash(), PromotedAt = DateTimeOffset.UtcNow,
            Confidence = AgentMemoryConfidence.High, SourceRefs = [sourceRef]
        };
        await services.GetRequiredService<IAgentMemoryStore>().SaveMemoryAsync(memory);
        var memoryHandle = await IssueHandleAsync(services, principal, AgentMemoryResourceKind.Memory, memory.MemoryId, "seed-memory");

        execution.Set("build-1");
        var build = await InvokeAsync(services, AgentMemoryToolCapabilityIds.BuildPack, new { MemoryHandles = new[] { memoryHandle }, Kinds = Array.Empty<string>(), Tags = Array.Empty<string>(), MaximumCount = 4, CharacterBudget = 1024, MinimumConfidence = "unknown" });
        build.IsSuccess.Should().BeTrue($"{build.Kind}/{build.Code}: {build.Message} ({string.Join(',', build.Issues.Select(issue => issue.Code))})");
        var buildResult = build.StructuredOutput!.Value;
        buildResult.GetProperty("Items").GetArrayLength().Should().Be(1);
        var grantId = buildResult.GetProperty("Items")[0].GetProperty("SourceGrants")[0].GetProperty("GrantId").GetString();
        grantId.Should().NotBeNullOrWhiteSpace();

        execution.Set("expand-1");
        var expanded = await InvokeAsync(services, AgentMemoryToolCapabilityIds.ExpandSource, new { GrantId = grantId, MaximumCharacters = 1024 });
        expanded.IsSuccess.Should().BeTrue(expanded.Message);
        expanded.StructuredOutput!.Value.GetProperty("SanitizedContent").GetString().Should().Contain("first turn");
        expanded.StructuredOutput!.Value.GetProperty("SanitizedContent").GetString().Should().NotContain("adjacent turn");

        var historyHandle = await services.GetRequiredService<IAgentMemoryHistoryResourceHandleIssuer>().IssueAsync(
            new AgentMemoryHostArtifactBatchKey { HostOperationId = "host-history", OperationFingerprint = HostHash("history-plan"), ArtifactPurpose = "history" },
            principal, AgentMemoryHistorySourceKind.Conversation, conversation.ConversationId);
        execution.Set("compress-1");
        var compressed = await InvokeAsync(services, AgentMemoryToolCapabilityIds.CompressHistory, new { HistorySourceHandle = historyHandle });
        compressed.IsSuccess.Should().BeTrue(
            $"{compressed.Kind}/{compressed.Code}: {compressed.Message}; output={compressed.StructuredOutput?.GetRawText()}");
        var contextHandle = compressed.StructuredOutput!.Value.GetProperty("ContextHandle").GetString();

        execution.Set("extract-1");
        var extracted = await InvokeAsync(services, AgentMemoryToolCapabilityIds.ExtractCandidates, new { ContextHandle = contextHandle });
        extracted.IsSuccess.Should().BeTrue(extracted.Message);
        var firstCandidate = extracted.StructuredOutput!.Value.GetProperty("Candidates")[0].GetProperty("CandidateHandle").GetString();
        execution.Set("extract-2");
        var extractedAgain = await InvokeAsync(services, AgentMemoryToolCapabilityIds.ExtractCandidates, new { ContextHandle = contextHandle });
        extractedAgain.IsSuccess.Should().BeTrue(extractedAgain.Message);
        var rejectedCandidate = extractedAgain.StructuredOutput!.Value.GetProperty("Candidates")[0].GetProperty("CandidateHandle").GetString();

        execution.Set("reject-1");
        var rejected = await InvokeAsync(services, AgentMemoryToolCapabilityIds.RejectCandidate, new { CandidateHandle = rejectedCandidate, Explanation = "e2e reject" });
        rejected.IsSuccess.Should().BeTrue(rejected.Message);
        rejected.StructuredOutput!.Value.GetProperty("OperationStatus").GetString().Should().Be("completed");
        rejected.StructuredOutput!.Value.GetProperty("CandidateStatus").GetString().Should().Be("rejected");

        execution.Set("extract-3");
        var extractedForSupersede = await InvokeAsync(services, AgentMemoryToolCapabilityIds.ExtractCandidates, new { ContextHandle = contextHandle });
        extractedForSupersede.IsSuccess.Should().BeTrue(extractedForSupersede.Message);
        var replacementCandidate = extractedForSupersede.StructuredOutput!.Value.GetProperty("Candidates")[0].GetProperty("CandidateHandle").GetString();

        execution.Set("promote-1");
        var promoted = await InvokeAsync(services, AgentMemoryToolCapabilityIds.PromoteCandidate, new { CandidateHandle = firstCandidate, Explanation = "e2e" });
        promoted.IsSuccess.Should().BeTrue(promoted.Message);
        var activeHandle = promoted.StructuredOutput!.Value.GetProperty("Item").GetProperty("MemoryHandle").GetString();
        var replayedPromotion = await InvokeAsync(services, AgentMemoryToolCapabilityIds.PromoteCandidate, new { CandidateHandle = firstCandidate, Explanation = "e2e" });
        replayedPromotion.IsSuccess.Should().BeTrue(replayedPromotion.Message);
        replayedPromotion.StructuredOutput.Should().Be(promoted.StructuredOutput);

        execution.Set("supersede-1");
        var superseded = await InvokeAsync(services, AgentMemoryToolCapabilityIds.SupersedeItem, new { MemoryHandle = activeHandle, ReplacementCandidateHandle = replacementCandidate, Explanation = "e2e" });
        superseded.IsSuccess.Should().BeTrue(superseded.Message);
        superseded.StructuredOutput!.Value.GetProperty("OperationStatus").GetString().Should().Be("completed");

        await host.StopAsync();
    }

    private static async Task<AgentToolInvocationOutcome> InvokeAsync(IServiceProvider services, string tool, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>()
            .InvokeAsync(new AgentToolInvocationRequest(tool, document.RootElement.Clone()));
    }

    private static async Task<string> IssueHandleAsync(IServiceProvider services, AgentMemoryToolPrincipal principal, AgentMemoryResourceKind kind, string resourceId, string purpose)
    {
        var newPrincipal = new AgentMemoryAccessPrincipal
        {
            TenantId = principal.TenantId, UserId = principal.UserId,
            CallerKind = AgentMemoryCallerKind.AgentTool, CallerId = principal.AgentId,
            SecurityContextId = principal.ExecutionId
        };
        var origin = new AgentMemoryArtifactOrigin
        {
            Kind = AgentMemoryArtifactOriginKind.AgentToolInvocation,
            OperationId = "e2e-tool-invocation",
            BindingHash = TestHash("e2e-tool-origin")
        };
        var scope = await services.GetRequiredService<IAgentMemoryAccessScopeProvider>().ResolveAsync(newPrincipal);
        var now = DateTimeOffset.UtcNow;
        var handle = new AgentMemoryAccessResourceHandle
        {
            HandleId = Guid.NewGuid().ToString("N"), ResourceKind = kind, ResourceId = resourceId, Principal = newPrincipal,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            IsUnscoped = scope.AllowUnscopedMemory, IssuingOperationId = "e2e-tool-invocation", IssuedAt = now,
            ExpiresAt = now.Add(scope.ResourceHandleLifetime), RequiredDescriptorRefs = scope.VisibleDescriptorRefs
        };
        var prepared = await services.GetRequiredService<IAgentMemoryAccessArtifactCoordinator>().PrepareAsync(
            newPrincipal, origin, scope, purpose, 0, [handle], []);
        return prepared.Handles!.Handles[0].HandleId;
    }

    private static CanonicalHash MemoryHash() => new()
    {
        Value = new string('a', 64), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent, Scope = "TenantVisible",
        Purpose = CanonicalHashPurposeNames.SourceIdentity, ContractVersion = "memory-hash-v2",
        CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryContentV2
    };

    private static CanonicalHash TestHash(string value) => new()
    {
        Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant(), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "test", Scope = "TenantVisible", Purpose = "Test",
        ContractVersion = "test-v1", CanonicalShapeVersion = "test-v1"
    };

    private static CanonicalHash HostHash(string value) => new()
    {
        Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant(), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "agent-memory-host-operation", Scope = "TenantVisible", Purpose = "HostOperation",
        ContractVersion = "memory-security-artifact-v2", CanonicalShapeVersion = "agent-memory-host-operation-v1"
    };

    private sealed class FixtureScopeProvider : IAgentMemoryToolAccessScopeProvider
    {
        public ValueTask<AgentMemoryToolAccessScope> ResolveAsync(AgentMemoryToolPrincipal principal, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentMemoryToolAccessScope
            {
                AllowUnscopedMemory = true,
                MaxRecallCount = 32,
                MaxRecallCharacters = 32_000,
                MaxExpansionCharacters = 16_000,
                MaxActiveResourceHandlesPerResource = 64,
                MaxGrantsPerResource = 64,
                MaxResourceHandlesPerInvocation = 128,
                MaxGrantsPerInvocation = 256,
                VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
                ResourceHandleLifetime = TimeSpan.FromMinutes(5),
                ExpansionGrantLifetime = TimeSpan.FromMinutes(5),
            });
    }

    private sealed class FixtureModuleSelection : IAgentToolModuleSelection { public string ModuleId => "fixture-json"; }

    private sealed class FixtureJsonContributor : IAgentToolJsonContextContributor
    {
        public string Id => "fixture-json";
        public int Order => 300;
        public string ModuleId => "fixture-json";
        public IReadOnlyCollection<Type> BindingRootTypes => Array.Empty<Type>();
        public JsonSerializerContext Create(JsonSerializerOptions sharedOptions) => new FixtureJsonContext(sharedOptions);
    }

    [JsonSerializable(typeof(FixtureMarker))]
    private partial class FixtureJsonContext : JsonSerializerContext;
    private sealed record FixtureMarker(string Value);

    private sealed class FixtureHistoryAuthorizer : IAgentMemoryHistoryAccessAuthorizer
    {
        public ValueTask<bool> IsAuthorizedAsync(AgentMemoryToolPrincipal principal, AgentMemoryToolAccessScope scope, AgentMemoryHistorySourceKind sourceKind, string sourceId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class FixtureApprovalGate : IAgentToolApprovalGate
    {
        public ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(AgentToolApprovalRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentToolApprovalResult { Decision = AgentToolApprovalDecision.Approved, ClaimState = AgentToolApprovalEvidenceClaimState.Claimed, EvidenceId = "fixture-approval", ApproverReference = "fixture" });
    }

    private sealed class FixtureExecutionContextAccessor : IAgentExecutionContextAccessor
    {
        public AgentExecutionContext? Current { get; private set; }
        public void Set(string invocationId) => Current = new AgentExecutionContext
        {
            ExecutionId = "e2e-execution", InvocationId = invocationId, AgentId = "e2e-agent",
            AgentRoles = new HashSet<string>(["memory-reader", "memory-processor", "memory-curator"]), CallOrigin = AgentToolCallOrigin.ExplicitRequest
        };
    }

    private sealed class FixtureTenantContext : ITenantContext { public string? CurrentTenantId => "e2e-tenant"; }

    private sealed class FixturePermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);
        public Task<bool> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string permissionName) => Task.FromResult(true);
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(permissionNames.ToDictionary(name => name, _ => true, StringComparer.Ordinal)));
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string[] permissionNames)
            => IsGrantedAsync(permissionNames);
        public Task CheckAsync(string permissionName) => Task.CompletedTask;
    }

    private sealed class FixtureCurrentUser : ICurrentUser
    {
        public string Id => "e2e-user"; public string UserName => "e2e-user"; public bool IsAuthenticated => true; public string TenantId => "e2e-tenant";
        public string[] Roles => ["memory-reader", "memory-processor", "memory-curator"]; public Guid? OrganizationId => null; public IReadOnlyList<Guid> OrganizationIds => [];
        public int DataScopeValue => 0; public bool IsSuperAdmin => false; public string FindClaimValue(string claimType) => string.Empty; public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => Roles.Contains(roleName); public bool IsInOrganization(Guid orgId) => false;
    }
}
