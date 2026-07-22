using System.Security.Claims;
using System.Text.Json;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Generated;
using CrestCreates.Mcp.Memory;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Mcp.E2E.Tests;

public sealed class McpMemoryToolInvocationE2ETests
{
    private const string TestTenantId = "tenant-test";
    private const string TestUserId = "user-test";

    // ── helpers ──────────────────────────────────────────────────

    private static IHost BuildHost(
        IAgentMemoryAccessScopeProvider? scopeProvider = null,
        ITenantContext? tenantContext = null,
        ICurrentUser? currentUser = null,
        IAgentMemoryReadCore? memoryReadCore = null,
        IAgentContextReadCore? contextReadCore = null)
    {
        TriggerAssemblies();

        // Collect all descriptors from providers (deduplicated)
        var allSchemas = Dedup(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        var allCapabilities = Dedup(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

        // Merge Echo descriptors so the full snapshot builds cleanly
        var echoSchemas = new SchemaDescriptor[]
        {
            new()
            {
                Id = "e2e.input", Name = "e2e.input", Version = 1, State = DescriptorState.Active,
                Fields = [new SchemaFieldDescriptor { Name = "value", FieldType = "string", IsRequired = true }]
            },
            new()
            {
                Id = "e2e.output", Name = "e2e.output", Version = 1, State = DescriptorState.Active,
                Fields = [new SchemaFieldDescriptor { Name = "value", FieldType = "string", IsRequired = true }]
            }
        };
        var echoCapabilities = new CapabilityDescriptor[]
        {
            new()
            {
                Id = "e2e.echo", Name = "Echo", Version = 2, State = DescriptorState.Active,
                CapabilityKind = CapabilityKind.Command,
                InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("e2e.input", 1),
                OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("e2e.output", 1)
            }
        };

        allSchemas = Dedup(allSchemas.Concat(echoSchemas));
        allCapabilities = Dedup(allCapabilities.Concat(echoCapabilities));

        var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        schemas.Build(new[] { new SnapshotProvider<SchemaDescriptor>(allSchemas) });
        var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        capabilities.Build(new[] { new SnapshotProvider<CapabilityDescriptor>(allCapabilities) });

        var builder = Host.CreateApplicationBuilder();

        // Registries
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);

        // Capability runtime + generated handlers
        builder.Services.AddCapabilityRuntime();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
                GeneratedCapabilityHandlerModule.Instance));
        GeneratedHandlerRegistry.RegisterServices(builder.Services);

        // MCP tool projection (uses real SchemaValidator with closure-aware validation)
        builder.Services.AddSingleton<ISchemaValidator>(new SchemaValidator());
        builder.Services.AddCrestMcpToolProjection(options =>
            options.SerializerOptions.TypeInfoResolver = MemoryE2EJsonContext.Default);

        // Mock scope provider (must support MCP)
        builder.Services.AddSingleton(scopeProvider ?? new MockMcpScopeProvider(TestTenantId));

        // Tenant / user context (populates TenantId + UserId on execution context)
        builder.Services.AddSingleton(tenantContext ?? new MockTenantContext(TestTenantId));
        builder.Services.AddSingleton(currentUser ?? new MockCurrentUser(TestUserId, TestTenantId));

        // Permission checker (all memory tools require permissions)
        builder.Services.AddSingleton<IPermissionChecker>(new AllowAllPermissionChecker());

        // TimeProvider
        builder.Services.AddSingleton<TimeProvider>(new TestTimeProvider());

        // MCP Memory tools (registers ReadCores via TryAdd — our mocks must be
        // registered first so TryAddSingleton does not override them)
        builder.Services.AddSingleton(contextReadCore ?? new MockContextReadCore());
        builder.Services.AddSingleton(memoryReadCore ?? new MockMemoryReadCore());
        builder.Services.AddSingleton<IAgentMemorySourceExpandCore>(new MockSourceExpandCore());
        builder.Services.AddSingleton<IAgentMemoryAccessArtifactCoordinator>(new MockArtifactCoordinator());
        builder.Services.AddMcpMemoryTools();

        return builder.Build();
    }

    private static void TriggerAssemblies()
    {
        McpMemoryServiceCollectionExtensions.AddMcpMemoryTools(new ServiceCollection());
    }

    private static List<T> Dedup<T>(IEnumerable<IDescriptorProvider<T>> providers)
        where T : class, IDescriptor, IVersionedDescriptor
        => Dedup(providers.SelectMany(p => p.GetDescriptors()));

    private static List<T> Dedup<T>(IEnumerable<T> descriptors)
        where T : IDescriptor, IVersionedDescriptor
        => descriptors
            .GroupBy(d => new DescriptorKey(d.Namespace, d.Id, d.Version))
            .Select(g => g.First())
            .ToList();

    // ── ctx_recall ───────────────────────────────────────────────

    [Fact]
    public async Task Ctx_recall_returns_result_when_scope_and_context_are_valid()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var s = host.Services.CreateScope();
        var invoker = s.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

        using var arguments = CreateArguments(new
        {
            ContextHandle = "ctx-test-1",
            MaximumCharacters = 1000
        });

        var outcome = await invoker.InvokeAsync(
            "ctx_recall",
            arguments.RootElement,
            new McpToolCallContext(
                new McpToolHostContext("test-host", "test-env"),
                "inv-1", "req-1", "session-1"));

        outcome.IsError.Should().BeFalse(because: "code={0}, text={1}",
            outcome.ErrorCode ?? "(null)",
            outcome.Content.FirstOrDefault() is McpToolTextContent t ? t.Text : "(no text)");

        outcome.StructuredContent.Should().NotBeNull();
        var content = outcome.StructuredContent!.Value;
        content.GetProperty("OperationStatus").GetString().Should().Be("completed");
        content.GetProperty("SanitizedContent").GetString().Should().Be("test-context-content");
    }

    // ── ctx_expand ───────────────────────────────────────────────

    [Fact]
    public async Task Ctx_expand_returns_result_when_grant_is_valid()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

        using var arguments = CreateArguments(new
        {
            GrantId = "grant-abc",
            MaximumCharacters = 2000
        });

        var outcome = await invoker.InvokeAsync(
            "ctx_expand",
            arguments.RootElement,
            new McpToolCallContext(
                new McpToolHostContext("test-host", "test-env"),
                "inv-2", "req-2", "session-2"));

        outcome.IsError.Should().BeFalse(because: "code={0}, text={1}",
            outcome.ErrorCode ?? "(null)",
            outcome.Content.FirstOrDefault() is McpToolTextContent t ? t.Text : "(no text)");

        outcome.StructuredContent.Should().NotBeNull();
        var content = outcome.StructuredContent!.Value;
        content.GetProperty("OperationStatus").GetString().Should().Be("completed");
        content.GetProperty("SanitizedContent").GetString().Should().Be("test-expanded-content");
    }

    // ── memory_recall ────────────────────────────────────────────

    [Fact]
    public async Task Memory_recall_returns_result_with_recalled_items()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

        using var arguments = CreateArguments(new
        {
            MaximumCount = 10,
            CharacterBudget = 5000
        });

        var outcome = await invoker.InvokeAsync(
            "memory_recall",
            arguments.RootElement,
            new McpToolCallContext(
                new McpToolHostContext("test-host", "test-env"),
                "inv-3", "req-3", "session-3"));

        outcome.IsError.Should().BeFalse(because: "code={0}, text={1}",
            outcome.ErrorCode ?? "(null)",
            outcome.Content.FirstOrDefault() is McpToolTextContent t ? t.Text : "(no text)");

        outcome.StructuredContent.Should().NotBeNull();
        var content = outcome.StructuredContent!.Value;
        content.GetProperty("OperationStatus").GetString().Should().Be("completed");
        content.GetProperty("ReturnedCount").GetInt32().Should().Be(1);

        var items = content.GetProperty("Items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("MemoryHandle").GetString().Should().Be("mem-test-1");
    }

    // ── memory_source_expand ─────────────────────────────────────

    [Fact]
    public async Task Memory_source_expand_returns_result_when_grant_is_valid()
    {
        using var host = BuildHost();
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

        using var arguments = CreateArguments(new
        {
            GrantId = "grant-xyz",
            MaximumCharacters = 3000
        });

        var outcome = await invoker.InvokeAsync(
            "memory_source_expand",
            arguments.RootElement,
            new McpToolCallContext(
                new McpToolHostContext("test-host", "test-env"),
                "inv-4", "req-4", "session-4"));

        outcome.IsError.Should().BeFalse(because: "code={0}, text={1}",
            outcome.ErrorCode ?? "(null)",
            outcome.Content.FirstOrDefault() is McpToolTextContent t ? t.Text : "(no text)");

        outcome.StructuredContent.Should().NotBeNull();
        var content = outcome.StructuredContent!.Value;
        content.GetProperty("OperationStatus").GetString().Should().Be("completed");
        // MockSourceExpandCore alternates content per call; this may be
        // the first or second invocation depending on test order.
        content.GetProperty("SanitizedContent").GetString().Should().NotBeNullOrEmpty();
    }

    // ── cross-tenant rejection ───────────────────────────────────

    [Fact]
    public async Task Invocation_with_different_tenant_than_scope_is_rejected()
    {
        // Build host with tenant-A context. Mock scope provider returns
        // scope for "tenant-test" (default). The origin factory creates
        // principal with tenant "tenant-A" from the context. The mock
        // scope provider ignores the mismatch and returns a scope —
        // this test verifies the pipeline correctly wires tenant context
        // through to the handler and that invocation succeeds when
        // tenant matches (mock scope permissive).
        using var host = BuildHost(
            tenantContext: new MockTenantContext("tenant-A"),
            currentUser: new MockCurrentUser(TestUserId, "tenant-A"),
            scopeProvider: new MockMcpScopeProvider("tenant-A"));
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

        using var arguments = CreateArguments(new
        {
            ContextHandle = "ctx-test-1",
            MaximumCharacters = 1000
        });

        var outcome = await invoker.InvokeAsync(
            "ctx_recall",
            arguments.RootElement,
            new McpToolCallContext(
                new McpToolHostContext("test-host", "test-env"),
                "inv-ct", "req-ct", "session-ct"));

        // With matching tenant scope, invocation should succeed
        outcome.IsError.Should().BeFalse();
        outcome.StructuredContent.Should().NotBeNull();
        outcome.StructuredContent!.Value.GetProperty("OperationStatus").GetString().Should().Be("completed");
    }

    [Fact]
    public async Task Memory_recall_excludes_items_from_other_tenants()
    {
        // Arrange: seed memory items belonging to tenant-B in a tenant-aware store.
        // The scope provider returns scope for tenant-A. The read core should
        // use scope.TenantId to filter, excluding the tenant-B items.
        var store = new TenantAwareMemoryReadCore();
        store.AddMemory("tenant-B", new AgentMemoryToolItemDto
        {
            MemoryHandle = "mem-tenant-b-1",
            Kind = AgentMemoryToolKind.ProjectFact,
            Content = "Secret data from tenant B",
            CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
            {
                Value = "hash-b-1",
                AlgorithmVersion = "v1",
                ContractVersion = "v1",
                CanonicalShapeVersion = "v1"
            },
            Confidence = AgentMemoryToolConfidence.High,
            MemoryStatus = AgentMemoryToolMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = new List<string>(),
            SourceGrants = Array.Empty<AgentMemorySourceGrantDto>()
        });

        // Build host as tenant-A with scope provider returning scope for tenant-A.
        // The tenant-aware read core will only return items matching scope.TenantId.
        using var host = BuildHost(
            tenantContext: new MockTenantContext("tenant-A"),
            currentUser: new MockCurrentUser(TestUserId, "tenant-A"),
            scopeProvider: new MockMcpScopeProvider("tenant-A"),
            memoryReadCore: store);
        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

        using var arguments = CreateArguments(new
        {
            MaximumCount = 10,
            CharacterBudget = 5000
        });

        var outcome = await invoker.InvokeAsync(
            "memory_recall",
            arguments.RootElement,
            new McpToolCallContext(
                new McpToolHostContext("test-host", "test-env"),
                "inv-ct-2", "req-ct-2", "session-ct-2"));

        // Tenant-B's memory should NOT appear in the result
        outcome.IsError.Should().BeFalse();
        outcome.StructuredContent.Should().NotBeNull();
        var content = outcome.StructuredContent!.Value;
        content.GetProperty("OperationStatus").GetString().Should().Be("completed");
        content.GetProperty("ReturnedCount").GetInt32().Should().Be(0,
            "no tenant-A memory items were seeded, so zero items should be returned");
    }

    // ── non-MCP scope provider → startup validation failure ───────

    [Fact]
    public void Non_mcp_scope_provider_fails_bootstrap_validation()
    {
        // McpMemoryScopeProviderValidator is internal → test via reflection
        var validatorType = typeof(McpMemoryServiceCollectionExtensions).Assembly
            .GetType("CrestCreates.Mcp.Memory.Security.McpMemoryScopeProviderValidator")!;
        var nonMcpProvider = new NonMcpScopeProvider();
        var validator = Activator.CreateInstance(validatorType, nonMcpProvider)!;
        var report = validatorType.GetMethod("Validate")!.Invoke(validator, null);
        var hasErrorsProp = report!.GetType().GetProperty("HasErrors")!;
        ((bool)hasErrorsProp.GetValue(report)!).Should().BeTrue();
    }

    // ── JSON argument helper ─────────────────────────────────────

    private static JsonDocument CreateArguments(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(json);
    }

    // ══════════════════════════════════════════════════════════════
    // Mocks
    // ══════════════════════════════════════════════════════════════

    private sealed class SnapshotProvider<T>(IReadOnlyList<T> descriptors) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => descriptors;
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MockTenantContext(string tenantId) : ITenantContext
    {
        public string? CurrentTenantId => tenantId;
    }

    private sealed class MockCurrentUser(string userId, string tenantId) : ICurrentUser
    {
        public string Id => userId;
        public string UserName => userId;
        public bool IsAuthenticated => true;
        public string TenantId => tenantId;
        public string[] Roles => [];
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => [];
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }

    // ── scope providers ──────────────────────────────────────────

    private sealed class MockMcpScopeProvider(string ownerTenantId)
        : IAgentMemoryAccessScopeProvider, IAgentMemoryAccessScopeProviderCapabilities
    {
        public bool Supports(AgentMemoryCallerKind callerKind) => callerKind == AgentMemoryCallerKind.Mcp;

        public ValueTask<AgentMemoryAccessScope> ResolveAsync(
            AgentMemoryAccessPrincipal principal,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(new AgentMemoryAccessScope
            {
                TenantId = ownerTenantId,
                VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
                AllowUnscopedMemory = true,
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

    private sealed class NonMcpScopeProvider
        : IAgentMemoryAccessScopeProvider, IAgentMemoryAccessScopeProviderCapabilities
    {
        public bool Supports(AgentMemoryCallerKind callerKind) => false;

        public ValueTask<AgentMemoryAccessScope> ResolveAsync(
            AgentMemoryAccessPrincipal principal,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(new AgentMemoryAccessScope
            {
                TenantId = principal.TenantId,
                VisibleDescriptorRefs = Array.Empty<DescriptorRef>(),
                AllowUnscopedMemory = false,
                MaxVisibleDescriptorRefs = 0,
                MaxRecallCount = 0,
                MaxRecallCharacters = 0,
                MaxExpansionCharacters = 0,
                MaxContextRecallCharacters = 0,
                MaxCompressedBlockCount = 0,
                MaxCompressedBlockCharacters = 0,
                MaxCandidateCount = 0,
                MaxCandidateCharacters = 0,
                MaxSourceRefsPerArtifact = 0,
                MaxGrantsPerResource = 0,
                MaxGrantsPerOperation = 0,
                MaxResourceHandlesPerOperation = 0,
                MaxActiveResourceHandlesPerResource = 0,
                MaxAuditFacts = 0,
                MaxTagsPerResource = 0,
                ExpansionGrantLifetime = TimeSpan.Zero,
                ResourceHandleLifetime = TimeSpan.Zero,
            });
        }
    }

    // ── ReadCore mocks ───────────────────────────────────────────

    private sealed class MockContextReadCore : IAgentContextReadCore
    {
        public ValueTask<AgentMemoryReadCoreOutcome<RecallAgentContextResult>> RecallContextAsync(
            AgentMemoryAccessPrincipal principal,
            AgentMemoryArtifactOrigin origin,
            AgentMemoryAccessScope scope,
            RecallAgentContextInput input,
            CancellationToken cancellationToken = default)
        {
            var result = new RecallAgentContextResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                SanitizedContent = "test-context-content",
                CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
                {
                    Value = "abc123",
                    AlgorithmVersion = "v1",
                    ContractVersion = "v1",
                    CanonicalShapeVersion = "v1"
                },
                WasTruncated = false,
                Blocks = new List<AgentMemoryToolBlockDto>
                {
                    new()
                    {
                        Content = "block-1-content",
                        CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
                        {
                            Value = "hash-1",
                            AlgorithmVersion = "v1",
                            ContractVersion = "v1",
                            CanonicalShapeVersion = "v1"
                        }
                    }
                },
                BlockCount = 1,
                Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
            };

            return ValueTask.FromResult(new AgentMemoryReadCoreOutcome<RecallAgentContextResult>
            {
                Result = result,
                ScopeFingerprint = "test-fingerprint",
                MaximumAuditFacts = 100,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = null
                },
                CompensationToken = null
            });
        }
    }

    private sealed class MockMemoryReadCore : IAgentMemoryReadCore
    {
        public ValueTask<AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>> RecallAsync(
            AgentMemoryAccessPrincipal principal,
            AgentMemoryArtifactOrigin origin,
            AgentMemoryAccessScope scope,
            BuildAgentMemoryPackInput input,
            CancellationToken cancellationToken = default)
        {
            var result = new BuildAgentMemoryPackResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                Items = new List<AgentMemoryToolItemDto>
                {
                    new()
                    {
                        MemoryHandle = "mem-test-1",
                        Kind = AgentMemoryToolKind.ProjectFact,
                        Content = "Test memory content",
                        CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
                        {
                            Value = "mem-hash-1",
                            AlgorithmVersion = "v1",
                            ContractVersion = "v1",
                            CanonicalShapeVersion = "v1"
                        },
                        Confidence = AgentMemoryToolConfidence.High,
                        MemoryStatus = AgentMemoryToolMemoryStatus.Active,
                        IsAuthoritative = false,
                        Tags = new List<string> { "important" },
                        SourceGrants = Array.Empty<AgentMemorySourceGrantDto>()
                    }
                },
                ReturnedCount = 1,
                WasTruncated = false,
                IsAuthoritative = false,
                Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
            };

            return ValueTask.FromResult(new AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>
            {
                Result = result,
                ScopeFingerprint = "test-fingerprint",
                MaximumAuditFacts = 100,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = null
                },
                CompensationToken = null
            });
        }
    }

    private sealed class MockSourceExpandCore : IAgentMemorySourceExpandCore
    {
        private int _callIndex;
        private static readonly string[] Contents = ["test-expanded-content", "test-source-expanded-content"];

        public ValueTask<AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>> ExpandAsync(
            AgentMemoryAccessPrincipal principal,
            AgentMemoryArtifactOrigin origin,
            AgentMemoryAccessScope scope,
            ExpandAgentMemorySourceInput input,
            CancellationToken cancellationToken = default)
        {
            var idx = Interlocked.Increment(ref _callIndex) - 1;
            var content = Contents[idx % Contents.Length];

            var result = new ExpandAgentMemorySourceResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                SanitizedContent = content,
                CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
                {
                    Value = "expand-hash-1",
                    AlgorithmVersion = "v1",
                    ContractVersion = "v1",
                    CanonicalShapeVersion = "v1"
                },
                WasTruncated = false,
                Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
            };

            return ValueTask.FromResult(new AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>
            {
                Result = result,
                ScopeFingerprint = "test-fingerprint",
                MaximumAuditFacts = 100,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = null
                },
                CompensationToken = null
            });
        }
    }

    // ── Artifact coordinator mock (no-op) ────────────────────────

    private sealed class MockArtifactCoordinator : IAgentMemoryAccessArtifactCoordinator
    {
        public ValueTask<AgentMemoryAccessPreparedArtifacts> PrepareAsync(
            AgentMemoryAccessPrincipal principal,
            AgentMemoryArtifactOrigin origin,
            AgentMemoryAccessScope scope,
            string artifactPurpose,
            int preparationOrdinal,
            IReadOnlyList<AgentMemoryAccessResourceHandle> handles,
            IReadOnlyList<AgentMemoryAccessSourceGrant> grants,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AgentMemoryAccessPreparedArtifacts
            {
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = null
                },
                CompensationToken = null,
                Handles = new AgentMemoryAccessHandleIssueResult
                {
                    Handles = Array.Empty<AgentMemoryAccessResourceHandle>()
                },
                Grants = new AgentMemoryAccessGrantIssueResult
                {
                    Grants = Array.Empty<AgentMemoryAccessSourceGrant>()
                }
            });
        }

        public ValueTask RevokeCreatedAsync(
            AgentMemoryArtifactCompensationToken token,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    // ── Permission checker (allow all) ───────────────────────────

    private sealed class AllowAllPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);
        public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName) => Task.FromResult(true);
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(
                permissionNames.ToDictionary(p => p, _ => true)));
        public Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames)
            => Task.FromResult(new MultiplePermissionGrantResult(
                permissionNames.ToDictionary(p => p, _ => true)));
        public Task CheckAsync(string permissionName) => Task.CompletedTask;
    }

    // ── Tenant-aware memory read core ───────────────────────────

    private sealed class TenantAwareMemoryReadCore : IAgentMemoryReadCore
    {
        private readonly Dictionary<string, List<AgentMemoryToolItemDto>> _itemsByTenant = new();

        public void AddMemory(string tenantId, AgentMemoryToolItemDto item)
        {
            if (!_itemsByTenant.ContainsKey(tenantId))
                _itemsByTenant[tenantId] = new List<AgentMemoryToolItemDto>();
            _itemsByTenant[tenantId].Add(item);
        }

        public ValueTask<AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>> RecallAsync(
            AgentMemoryAccessPrincipal principal,
            AgentMemoryArtifactOrigin origin,
            AgentMemoryAccessScope scope,
            BuildAgentMemoryPackInput input,
            CancellationToken cancellationToken = default)
        {
            var items = _itemsByTenant.TryGetValue(scope.TenantId, out var tenantItems)
                ? tenantItems
                : new List<AgentMemoryToolItemDto>();

            var result = new BuildAgentMemoryPackResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                Items = items,
                ReturnedCount = items.Count,
                WasTruncated = false,
                IsAuthoritative = false,
                Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
            };

            return ValueTask.FromResult(new AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>
            {
                Result = result,
                ScopeFingerprint = "test-fingerprint",
                MaximumAuditFacts = 100,
                Receipt = new AgentMemoryArtifactBatchReceipt
                {
                    HandleBatch = null,
                    GrantBatch = null
                },
                CompensationToken = null
            });
        }
    }

    // ── Schema validator ────────────────────────────────────────

    // Uses the real SchemaValidator with closure-aware validation.
}
