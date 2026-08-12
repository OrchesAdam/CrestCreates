using System.Security.Claims;
using System.Text.Json;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Stores;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Accountability.Bootstrap;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Agent.Memory.Accountability.Bootstrap;
using CrestCreates.Agent.Memory.Accountability;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Mcp.Memory;
using CrestCreates.Mcp.Memory.AotFixture;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

return await McpMemoryAotFixtureRunner.RunAsync();

internal static class McpMemoryAotFixtureRunner
{
    private static CanonicalHash MakeContentHash() => new()
    {
        Value = "aot-hash-value",
        Algorithm = "SHA-256",
        AlgorithmVersion = "v1",
        ArtifactKind = "Memory",
        Scope = "InternalFull",
        Purpose = "Integrity",
        ContractVersion = "v1",
        CanonicalShapeVersion = "v1"
    };

    public static async Task<int> RunAsync()
    {
        try
        {
            // Trigger MCP Memory assembly load (module init registers providers).
            var dummy = new ServiceCollection();
            McpMemoryServiceCollectionExtensions.AddMcpMemoryTools(dummy);

            // Build registries from providers.
            var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
            schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
            var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
            capabilities.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

            // Build host with MCP tool projection + MCP Memory tools + real stores.
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton<ISchemaRegistry>(schemas);
            builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
            builder.Services.AddSingleton<IAgentMemoryAccessScopeProvider>(new AotScopeProvider());
            builder.Services.AddSingleton<ITenantContext>(new AotTenantContext("aot-tenant"));
            builder.Services.AddSingleton<ICurrentUser>(new AotCurrentUser("aot-user", "aot-tenant"));
            builder.Services.AddSingleton<IPermissionChecker, AotPermissionChecker>();
            builder.Services.AddAgentMemoryReadRuntime();
            builder.Services.AddCapabilityRuntime();
            builder.Services.AddAccountability();
            builder.Services.AddSingleton<InMemoryAuditSink>();
            builder.Services.AddSingleton<CrestCreates.Accountability.Abstractions.Sinks.IAuditSink>(
                sp => sp.GetRequiredService<InMemoryAuditSink>());
            builder.Services.AddAgentMemoryAccountability();
            builder.Services.AddCrestMcpToolProjection(options =>
            {
                options.SerializerOptions.TypeInfoResolver = McpMemoryAotJsonContext.Default;
            });
            builder.Services.AddMcpMemoryTools();

            // Register real in-memory stores for AOT validation.
            builder.Services.AddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>();
            builder.Services.AddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>();
            builder.Services.AddSingleton<IAgentTaskHistoryStore, InMemoryAgentTaskHistoryStore>();
            builder.Services.AddSingleton<IAgentCompressedContextStore, InMemoryAgentCompressedContextStore>();
            builder.Services.AddSingleton<IAgentMemoryContentSanitizer, AotContentSanitizer>();

            using var host = builder.Build();
            await host.StartAsync();

            // Verify the 4 MCP Memory tools are in the snapshot.
            var snapshot = host.Services.GetRequiredService<McpToolRuntimeSnapshotProvider>().GetRequired();
            if (snapshot.Find("ctx_recall") is null) return 2;
            if (snapshot.Find("ctx_expand") is null) return 3;
            if (snapshot.Find("memory_recall") is null) return 4;
            if (snapshot.Find("memory_source_expand") is null) return 5;

            // Verify JSON context resolves MCP Memory DTO types.
            var jsonOptions = new JsonSerializerOptions(McpMemoryAotJsonContext.Default.Options);
            jsonOptions.TypeInfoResolver = McpMemoryAotJsonContext.Default;
            if (jsonOptions.GetTypeInfo(typeof(RecallAgentContextInput)) is null) return 6;
            if (jsonOptions.GetTypeInfo(typeof(BuildAgentMemoryPackInput)) is null) return 7;
            if (jsonOptions.GetTypeInfo(typeof(ExpandAgentMemorySourceInput)) is null) return 8;

            // Seed real data into stores.
            var tenantId = "aot-tenant";
            var descA = new DescriptorRef { Id = "desc-a", Version = 1 };
            var contentHash = MakeContentHash();

            // Seed conversation.
            var conversationStore = host.Services.GetRequiredService<IAgentConversationStore>();
            var conversation = new AgentConversationRecord
            {
                ConversationId = "aot-conv",
                TenantId = tenantId,
                Turns = new[]
                {
                    new AgentConversationTurn
                    {
                        TurnId = "turn-0",
                        TenantId = tenantId,
                        Role = AgentConversationRole.User,
                        Content = "Hello from AOT fixture",
                        CreatedAt = DateTimeOffset.UtcNow,
                        DescriptorRefs = new[] { descA },
                        SourceRefs = Array.Empty<AgentContextSourceRef>()
                    }
                }
            };
            await conversationStore.SaveConversationAsync(conversation);

            // Seed a real compressed context for ctx_recall.
            var compressedContextStore = host.Services.GetRequiredService<IAgentCompressedContextStore>();
            await compressedContextStore.SaveCompressedContextAsync(new AgentCompressedContext
            {
                ContextId = "aot-context",
                TenantId = tenantId,
                Blocks =
                [
                    new AgentCompressedContextBlock
                    {
                        BlockId = "aot-context-block",
                        TenantId = tenantId,
                        Content = "Compressed context from AOT fixture",
                        CanonicalContentHash = contentHash,
                        SourceRefs =
                        [
                            new AgentContextSourceRef
                            {
                                SourceKind = AgentSourceKind.ConversationTurn,
                                TenantId = tenantId,
                                SourceId = "aot-conv",
                                DescriptorRefs = [descA]
                            }
                        ]
                    }
                ]
            });

            // Seed memory with a ConversationTurn source ref.
            var memoryStore = host.Services.GetRequiredService<IAgentMemoryStore>();
            var memory = new AgentMemoryItem
            {
                MemoryId = "aot-mem-1",
                TenantId = tenantId,
                Kind = AgentMemoryKind.ProjectFact,
                Content = "AOT fixture memory content",
                CanonicalContentHash = contentHash,
                PromotedAt = DateTimeOffset.UtcNow,
                DescriptorRefs = new[] { descA },
                SourceRefs = new[]
                {
                    new AgentContextSourceRef
                    {
                        SourceKind = AgentSourceKind.ConversationTurn,
                        TenantId = tenantId,
                        SourceId = "aot-conv",
                        DescriptorRefs = new[] { descA }
                    }
                },
                Confidence = AgentMemoryConfidence.High,
                Status = AgentMemoryStatus.Active
            };
            await memoryStore.SaveMemoryAsync(memory);

            // Issue handle and grant via Coordinator for ctx_recall and memory_source_expand.
            var coordinator = host.Services.GetRequiredService<IAgentMemoryAccessArtifactCoordinator>();
            var principal = new AgentMemoryAccessPrincipal
            {
                TenantId = tenantId,
                UserId = "aot-user",
                CallerKind = AgentMemoryCallerKind.Mcp,
                CallerId = "aot-fixture",
                SecurityContextId = "aot-session"
            };
            var origin = new AgentMemoryArtifactOrigin
            {
                Kind = AgentMemoryArtifactOriginKind.McpInvocation,
                BindingHash = contentHash,
                OperationId = "aot-op-1"
            };
            var scope = new AgentMemoryAccessScope
            {
                TenantId = tenantId,
                VisibleDescriptorRefs = new[] { descA },
                AllowUnscopedMemory = false,
                MaxVisibleDescriptorRefs = 100,
                MaxRecallCount = 100,
                MaxRecallCharacters = 100000,
                MaxExpansionCharacters = 100000,
                MaxContextRecallCharacters = 100000,
                MaxCompressedBlockCount = 100,
                MaxCompressedBlockCharacters = 100000,
                MaxCandidateCount = 100,
                MaxCandidateCharacters = 100000,
                MaxSourceRefsPerArtifact = 100,
                MaxGrantsPerResource = 100,
                MaxGrantsPerOperation = 100,
                MaxResourceHandlesPerOperation = 100,
                MaxActiveResourceHandlesPerResource = 100,
                MaxAuditFacts = 100,
                MaxTagsPerResource = 100,
                ExpansionGrantLifetime = TimeSpan.FromMinutes(5),
                ResourceHandleLifetime = TimeSpan.FromMinutes(30),
            };

            var scopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope);
            var now = DateTimeOffset.UtcNow;

            // Issue a Context Handle for ctx_recall.
            var contextHandle = new AgentMemoryAccessResourceHandle
            {
                HandleId = Guid.NewGuid().ToString("N"),
                ResourceKind = AgentMemoryResourceKind.Context,
                ResourceId = "aot-context",
                Principal = principal,
                RequiredDescriptorRefs = new[] { descA },
                IsUnscoped = false,
                ScopeFingerprint = scopeFingerprint,
                IssuingOperationId = "aot-op-1",
                IssuedAt = now,
                ExpiresAt = now.AddMinutes(30)
            };

            // Issue a Grant for memory_source_expand.
            var sourceGrant = new AgentMemoryAccessSourceGrant
            {
                GrantId = Guid.NewGuid().ToString("N"),
                SourceRef = new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.ConversationTurn,
                    TenantId = tenantId,
                    SourceId = "aot-conv",
                    DescriptorRefs = new[] { descA }
                },
                Principal = principal,
                RequiredDescriptorRefs = new[] { descA },
                IsUnscoped = false,
                ScopeFingerprint = scopeFingerprint,
                IssuingOperationId = "aot-op-1",
                IssuedAt = now,
                ExpiresAt = now.AddMinutes(5)
            };

            var prepared = await coordinator.PrepareAsync(
                principal, origin, scope, "aot-purpose", 0,
                new List<AgentMemoryAccessResourceHandle> { contextHandle },
                new List<AgentMemoryAccessSourceGrant> { sourceGrant },
                CancellationToken.None);

            var handleId = prepared.Handles!.Handles[0].HandleId;
            var grantId = prepared.Grants!.Grants[0].GrantId;

            // Invoke all 4 MCP Memory tools via IMcpToolInvoker.
            await using var invokerScope = host.Services.CreateAsyncScope();
            var invoker = invokerScope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

            var hostContext = new McpToolHostContext("aot-fixture", "Production");
            var allSucceeded = true;

            // ctx_recall with real handle.
            var ctxRecallResult = await InvokeToolAsync(invoker, hostContext,
                "ctx_recall", new RecallAgentContextInput { ContextHandle = handleId, MaximumBlockCount = 10, CharacterBudget = 100 });
            if (ctxRecallResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: ctx_recall (error={ctxRecallResult.ErrorCode})");
                allSucceeded = false;
            }
            else
            {
                Console.WriteLine($"ctx_recall: OK (content count={ctxRecallResult.Content.Count})");
            }

            // ctx_expand with real grant.
            var ctxExpandResult = await InvokeToolAsync(invoker, hostContext,
                "ctx_expand", new ExpandAgentMemorySourceInput { GrantId = grantId, MaximumCharacters = 100 });
            if (ctxExpandResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: ctx_expand (error={ctxExpandResult.ErrorCode})");
                allSucceeded = false;
            }
            else
            {
                Console.WriteLine($"ctx_expand: OK (content count={ctxExpandResult.Content.Count})");
            }

            // memory_recall (no handle needed — issues its own).
            var memoryRecallResult = await InvokeToolAsync(invoker, hostContext,
                "memory_recall", new BuildAgentMemoryPackInput { MaximumCount = 10, CharacterBudget = 1000 });
            if (memoryRecallResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: memory_recall (error={memoryRecallResult.ErrorCode})");
                allSucceeded = false;
            }
            else
            {
                Console.WriteLine($"memory_recall: OK (content count={memoryRecallResult.Content.Count})");
            }

            // memory_source_expand with real grant.
            var memoryExpandResult = await InvokeToolAsync(invoker, hostContext,
                "memory_source_expand", new ExpandAgentMemorySourceInput { GrantId = grantId, MaximumCharacters = 100 });
            if (memoryExpandResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: memory_source_expand (error={memoryExpandResult.ErrorCode})");
                allSucceeded = false;
            }
            else
            {
                Console.WriteLine($"memory_source_expand: OK (content count={memoryExpandResult.Content.Count})");
            }

            if (!allSucceeded)
            {
                Console.Error.WriteLine("AOT_FIXTURE_FAILED");
                return 1;
            }

            var accountabilityRecords = host.Services.GetRequiredService<InMemoryAuditSink>().GetRecords();
            var requiredMemoryPayloadKinds = new[]
            {
                AgentMemoryAccountabilityPayloadKinds.Recall,
                AgentMemoryAccountabilityPayloadKinds.SourceExpansion
            };
            if (requiredMemoryPayloadKinds.Any(kind =>
                    !accountabilityRecords.Any(record => record.Payload?.Kind == kind)))
            {
                Console.Error.WriteLine("FAIL: Accountability bridge did not persist a Memory fact");
                return 9;
            }
            var memoryRecords = accountabilityRecords
                .Where(record => record.Payload?.Kind is not null
                    && requiredMemoryPayloadKinds.Contains(record.Payload.Kind))
                .ToArray();
            if (memoryRecords.Any(record => string.IsNullOrWhiteSpace(record.CorrelationId)
                    || string.IsNullOrWhiteSpace(record.CausationId)
                    || string.IsNullOrWhiteSpace(record.ParentAuditId)))
            {
                Console.Error.WriteLine("FAIL: Memory facts did not preserve MCP causality");
                return 10;
            }
            Console.WriteLine("memory_accountability: OK");

            Console.WriteLine("MCP_MEMORY_NATIVEAOT_PIPELINE_OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UNHANDLED EXCEPTION IN AOT FIXTURE:");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static async Task<McpToolInvocationOutcome> InvokeToolAsync(
        IMcpToolInvoker invoker,
        McpToolHostContext hostContext,
        string toolName,
        object input)
    {
        var json = JsonSerializer.SerializeToElement(input, input.GetType(), McpMemoryAotJsonContext.Default);
        var callContext = new McpToolCallContext(
            hostContext,
            InvocationId: $"inv-{toolName}",
            RequestId: $"req-{toolName}",
            SessionId: "aot-session");
        return await invoker.InvokeAsync(toolName, json, callContext, CancellationToken.None);
    }

    private sealed class AotTenantContext(string tenantId) : ITenantContext
    {
        public string? CurrentTenantId => tenantId;
    }

    private sealed class AotCurrentUser(string userId, string tenantId) : ICurrentUser
    {
        public string Id => userId;
        public string UserName => userId;
        public bool IsAuthenticated => true;
        public string TenantId => tenantId;
        public string[] Roles => [];
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => [];
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }

    private sealed class AotPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);

        public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
            => Task.FromResult(true);

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(
                permissionNames.ToDictionary(permission => permission, _ => true)));

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(
            ClaimsPrincipal principal,
            string[] permissionNames)
            => IsGrantedAsync(permissionNames);

        public Task CheckAsync(string permissionName) => Task.CompletedTask;
    }

    private sealed class AotScopeProvider : IAgentMemoryAccessScopeProvider, IAgentMemoryAccessScopeProviderCapabilities
    {
        public bool Supports(AgentMemoryCallerKind callerKind) => callerKind == AgentMemoryCallerKind.Mcp;

        public ValueTask<AgentMemoryAccessScope> ResolveAsync(
            AgentMemoryAccessPrincipal principal,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(new AgentMemoryAccessScope
            {
                TenantId = principal.TenantId,
                VisibleDescriptorRefs = new[] { new DescriptorRef { Id = "desc-a", Version = 1 } },
                AllowUnscopedMemory = false,
                MaxVisibleDescriptorRefs = 100,
                MaxRecallCount = 100,
                MaxRecallCharacters = 100000,
                MaxExpansionCharacters = 100000,
                MaxContextRecallCharacters = 100000,
                MaxCompressedBlockCount = 100,
                MaxCompressedBlockCharacters = 100000,
                MaxCandidateCount = 100,
                MaxCandidateCharacters = 100000,
                MaxSourceRefsPerArtifact = 100,
                MaxGrantsPerResource = 100,
                MaxGrantsPerOperation = 100,
                MaxResourceHandlesPerOperation = 100,
                MaxActiveResourceHandlesPerResource = 100,
                MaxAuditFacts = 100,
                MaxTagsPerResource = 100,
                ExpansionGrantLifetime = TimeSpan.FromMinutes(5),
                ResourceHandleLifetime = TimeSpan.FromMinutes(30),
            });
        }
    }

    private sealed class AotContentSanitizer : IAgentMemoryContentSanitizer
    {
        public SanitizedAgentContent Sanitize(
            string tenantId, string content, IReadOnlyList<AgentContextSourceRef> sourceRefs)
        {
            return new SanitizedAgentContent
            {
                SanitizedContent = content,
                CanonicalContentHash = MakeContentHash()
            };
        }
    }
}
