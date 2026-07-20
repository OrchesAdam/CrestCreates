using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Tools;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

return await MemoryToolFixtureRunner.RunAsync();

internal static class MemoryToolFixtureRunner
{
    public static async Task<int> RunAsync()
    {
        try
        {
            var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
            schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
            var execution = new FixtureExecutionAccessor();
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
            builder.Services.AddCrestAgentTools();
            builder.Services.AddAgentMemoryTools();
            using var host = builder.Build();
            await host.StartAsync();
            var services = host.Services;
            var principal = new AgentMemoryToolPrincipal { TenantId = "aot-tenant", UserId = "aot-user", AgentId = "aot-agent", ExecutionId = "aot-execution" };
            var conversation = new AgentConversationRecord
            {
                ConversationId = "aot-conversation", TenantId = principal.TenantId,
                Turns = [
                    new AgentConversationTurn { TurnId = "turn-0", TenantId = principal.TenantId, Role = AgentConversationRole.User, Content = "first" },
                    new AgentConversationTurn { TurnId = "turn-1", TenantId = principal.TenantId, Role = AgentConversationRole.User, Content = "adjacent" }
                ]
            };
            var source = new AgentContextSourceRef { SourceKind = AgentSourceKind.ConversationTurn, TenantId = principal.TenantId, SourceId = conversation.ConversationId, RangeStart = 0, RangeEnd = 0, CanonicalContentHash = Hash() };
            await services.GetRequiredService<IAgentConversationStore>().SaveConversationAsync(conversation);
            await services.GetRequiredService<IAgentMemoryStore>().SaveMemoryAsync(new AgentMemoryItem
            {
                MemoryId = "aot-memory", TenantId = principal.TenantId, Kind = AgentMemoryKind.ProjectFact, Content = "first",
                CanonicalContentHash = Hash(), PromotedAt = DateTimeOffset.UtcNow, Confidence = AgentMemoryConfidence.High, SourceRefs = [source]
            });
            var scope = await services.GetRequiredService<IAgentMemoryToolAccessScopeProvider>().ResolveAsync(principal);
            var now = DateTimeOffset.UtcNow;
            var issued = await services.GetRequiredService<IAgentMemoryResourceHandleStore>().TryIssueBatchAsync(
                new AgentMemorySecurityArtifactBatchKey { OriginKind = AgentMemorySecurityArtifactBatchOriginKind.TrustedHostOperation, ArtifactPurpose = "aot-memory", PreparationOrdinal = 0, ArtifactPlanHash = "aot-memory" },
                [new AgentMemoryResourceHandle { HandleId = "aot-memory-handle", ResourceKind = AgentMemoryResourceKind.Memory, ResourceId = "aot-memory", Principal = principal, ScopeFingerprint = "aot-scope", IssuingInvocationId = "host", IssuedAt = now, ExpiresAt = now.Add(scope.ResourceHandleLifetime) }],
                scope.MaxActiveResourceHandlesPerResource);

            execution.Set("aot-build");
            var build = await InvokeAsync(services, AgentMemoryToolCapabilityIds.BuildPack, new BuildInput { MemoryHandles = [issued.Handles[0].HandleId], Kinds = [], Tags = [], MaximumCount = 4, CharacterBudget = 1024, MinimumConfidence = "unknown" }, FixtureJsonContext.Default.BuildInput);
            if (!build.IsSuccess) return 2;
            var grant = build.StructuredOutput!.Value.GetProperty("Items")[0].GetProperty("SourceGrants")[0].GetProperty("GrantId").GetString();
            execution.Set("aot-expand");
            var expanded = await InvokeAsync(services, AgentMemoryToolCapabilityIds.ExpandSource, new ExpandInput { GrantId = grant!, MaximumCharacters = 1024 }, FixtureJsonContext.Default.ExpandInput);
            if (!expanded.IsSuccess || expanded.StructuredOutput!.Value.GetProperty("SanitizedContent").GetString()?.Contains("adjacent", StringComparison.Ordinal) == true) return 3;
            var historyHandle = await services.GetRequiredService<IAgentMemoryHistoryResourceHandleIssuer>().IssueAsync(
                new AgentMemoryHostArtifactBatchKey { HostOperationId = "aot-history", OperationFingerprint = "aot-history-plan", ArtifactPurpose = "history" },
                principal, AgentMemoryHistorySourceKind.Conversation, conversation.ConversationId);
            execution.Set("aot-compress");
            var compressed = await InvokeAsync(services, AgentMemoryToolCapabilityIds.CompressHistory, new HistoryInput { HistorySourceHandle = historyHandle }, FixtureJsonContext.Default.HistoryInput);
            if (!compressed.IsSuccess) return 4;
            var contextHandle = compressed.StructuredOutput!.Value.GetProperty("ContextHandle").GetString();
            execution.Set("aot-extract");
            var extracted = await InvokeAsync(services, AgentMemoryToolCapabilityIds.ExtractCandidates, new ContextInput { ContextHandle = contextHandle! }, FixtureJsonContext.Default.ContextInput);
            if (!extracted.IsSuccess) return 5;
            var candidateHandle = extracted.StructuredOutput!.Value.GetProperty("Candidates")[0].GetProperty("CandidateHandle").GetString();
            execution.Set("aot-promote");
            var promoted = await InvokeAsync(services, AgentMemoryToolCapabilityIds.PromoteCandidate, new CandidateInput { CandidateHandle = candidateHandle!, Explanation = "aot" }, FixtureJsonContext.Default.CandidateInput);
            if (!promoted.IsSuccess) return 6;
            var replay = await InvokeAsync(services, AgentMemoryToolCapabilityIds.PromoteCandidate, new CandidateInput { CandidateHandle = candidateHandle!, Explanation = "aot" }, FixtureJsonContext.Default.CandidateInput);
            if (!replay.IsSuccess) return 7;
            Console.WriteLine("AGENT_MEMORY_TOOL_NATIVEAOT_PIPELINE_OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<AgentToolInvocationOutcome> InvokeAsync<T>(IServiceProvider services, string tool, T arguments, JsonTypeInfo<T> typeInfo)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments, typeInfo));
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAgentToolInvoker>().InvokeAsync(new AgentToolInvocationRequest(tool, json.RootElement.Clone()));
    }

    private static CanonicalHash Hash() => new()
    {
        Value = new string('b', 64), Algorithm = "SHA-256", AlgorithmVersion = "sha256-canonical-json-v1", ArtifactKind = CanonicalHashArtifactNames.AgentMemoryContent,
        Scope = "TenantVisible", Purpose = CanonicalHashPurposeNames.SourceIdentity, ContractVersion = "memory-hash-v2", CanonicalShapeVersion = AgentMemoryCanonicalShapeVersions.MemoryContentV2
    };
}

internal sealed record BuildInput
{
    public required string[] MemoryHandles { get; init; }
    public required string[] Kinds { get; init; }
    public required string[] Tags { get; init; }
    public required int MaximumCount { get; init; }
    public required int CharacterBudget { get; init; }
    public required string MinimumConfidence { get; init; }
}

internal sealed record ExpandInput
{
    public required string GrantId { get; init; }
    public required int MaximumCharacters { get; init; }
}

internal sealed record HistoryInput
{
    public required string HistorySourceHandle { get; init; }
}

internal sealed record ContextInput
{
    public required string ContextHandle { get; init; }
}

internal sealed record CandidateInput
{
    public required string CandidateHandle { get; init; }
    public required string Explanation { get; init; }
}

[JsonSerializable(typeof(BuildInput))]
[JsonSerializable(typeof(ExpandInput))]
[JsonSerializable(typeof(HistoryInput))]
[JsonSerializable(typeof(ContextInput))]
[JsonSerializable(typeof(CandidateInput))]
internal partial class FixtureJsonContext : JsonSerializerContext;

internal sealed class FixtureScopeProvider : IAgentMemoryToolAccessScopeProvider
{
    public ValueTask<AgentMemoryToolAccessScope> ResolveAsync(AgentMemoryToolPrincipal principal, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AgentMemoryToolAccessScope { AllowUnscopedMemory = true });
}
internal sealed class FixtureHistoryAuthorizer : IAgentMemoryHistoryAccessAuthorizer
{
    public ValueTask<bool> IsAuthorizedAsync(AgentMemoryToolPrincipal principal, AgentMemoryToolAccessScope scope, AgentMemoryHistorySourceKind sourceKind, string sourceId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
}
internal sealed class FixtureModuleSelection : IAgentToolModuleSelection { public string ModuleId => "aot-json"; }
internal sealed class FixtureJsonContributor : IAgentToolJsonContextContributor
{
    public string Id => "aot-json"; public int Order => 300; public string ModuleId => "aot-json"; public IReadOnlyCollection<Type> BindingRootTypes => Array.Empty<Type>();
    public JsonSerializerContext Create(JsonSerializerOptions sharedOptions) => new AotExtraJsonContext(sharedOptions);
}
[JsonSerializable(typeof(AotMarker))]
internal partial class AotExtraJsonContext : JsonSerializerContext;
internal sealed record AotMarker(string Value);
internal sealed class FixtureApprovalGate : IAgentToolApprovalGate
{
    public ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(AgentToolApprovalRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AgentToolApprovalResult { Decision = AgentToolApprovalDecision.Approved, ClaimState = AgentToolApprovalEvidenceClaimState.Claimed, EvidenceId = "aot-approval", ApproverReference = "fixture" });
}
internal sealed class FixtureExecutionAccessor : IAgentExecutionContextAccessor
{
    public AgentExecutionContext? Current { get; private set; }
    public void Set(string invocationId) => Current = new AgentExecutionContext { ExecutionId = "aot-execution", InvocationId = invocationId, AgentId = "aot-agent", AgentRoles = new HashSet<string>(["memory-reader", "memory-processor", "memory-curator"]), CallOrigin = AgentToolCallOrigin.ExplicitRequest };
}
internal sealed class FixtureTenantContext : ITenantContext { public string? CurrentTenantId => "aot-tenant"; }
internal sealed class FixtureCurrentUser : ICurrentUser
{
    public string Id => "aot-user"; public string UserName => "aot-user"; public bool IsAuthenticated => true; public string TenantId => "aot-tenant"; public string[] Roles => ["memory-reader", "memory-processor", "memory-curator"]; public Guid? OrganizationId => null; public IReadOnlyList<Guid> OrganizationIds => []; public int DataScopeValue => 0; public bool IsSuperAdmin => false; public string FindClaimValue(string claimType) => string.Empty; public string[] FindClaimValues(string claimType) => []; public bool IsInRole(string roleName) => Roles.Contains(roleName); public bool IsInOrganization(Guid orgId) => false;
}
internal sealed class FixturePermissionChecker : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true); public Task<bool> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string permissionName) => Task.FromResult(true);
    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames) => Task.FromResult(new MultiplePermissionGrantResult(permissionNames.ToDictionary(name => name, _ => true, StringComparer.Ordinal)));
    public Task<MultiplePermissionGrantResult> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal principal, string[] permissionNames) => IsGrantedAsync(permissionNames); public Task CheckAsync(string permissionName) => Task.CompletedTask;
}
