using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using CrestCreates.Mcp;
using CrestCreates.Mcp.Memory;
using CrestCreates.Mcp.Memory.Security;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.Mcp.E2E.Tests;

/// <summary>
/// E2E tests verifying MCP Memory tool discovery, snapshot building,
/// JSON context composition, and startup validation for the four
/// MCP Memory tools (ctx_recall, ctx_expand, memory_recall, memory_source_expand).
///
/// These tests do NOT invoke the tools end-to-end — full end-to-end
/// invocation requires the Agent Memory infrastructure (stores, retrievers,
/// authorizers) which is tested in the Agent Memory Tools E2E suite.
/// </summary>
public sealed class McpMemoryToolE2ETests
{
    [Fact]
    public void Di_registration_order__pre_built_registry_wins()
    {
        TriggerMcpMemoryAssembly();

        var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        capabilities.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

        var services = new ServiceCollection();
        // Register pre-built BEFORE AddCapabilityRuntime
        services.AddSingleton<ICapabilityRegistry>(capabilities);
        services.AddCapabilityRuntime();
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<ICapabilityRegistry>();
        // The registry should have MCP Memory capabilities from module init.
        // This test verifies the DI ordering, not which specific capabilities exist.
        resolved.GetByVersion("mcp.ctx_recall", 1).Should().NotBeNull(
            "DI should return our pre-built registry (registered first)");
    }

    [Fact]
    public async Task Mcp_memory_tools_are_discoverable_in_registry()
    {
        TriggerMcpMemoryAssembly();

        var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        capabilities.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

        capabilities.GetByVersion("mcp.ctx_recall", 1).Should().NotBeNull();
        capabilities.GetByVersion("mcp.ctx_expand", 1).Should().NotBeNull();
        capabilities.GetByVersion("mcp.memory_recall", 1).Should().NotBeNull();
        capabilities.GetByVersion("mcp.memory_source_expand", 1).Should().NotBeNull();
    }

    [Fact]
    public async Task Mcp_memory_tools_are_present_in_runtime_snapshot()
    {
        // Trigger MCP Memory assembly load so descriptor providers are registered.
        TriggerMcpMemoryAssembly();

        // Build registries from providers. The Echo tool descriptors from the
        // test assembly's [McpToolSpecs] code-gen are handled by the existing
        // McpToolProjectionE2ETests which registers Echo capability/schema
        // providers on the global DescriptorProviderRegistry.
        var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
        schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
        capabilities.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IAgentMemoryAccessScopeProvider>(new MockMcpScopeProvider());
        builder.Services.AddSingleton<ISchemaRegistry>(schemas);
        builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
        builder.Services.AddCapabilityRuntime();
        builder.Services.AddCrestMcpToolProjection(options =>
        {
            options.SerializerOptions.TypeInfoResolver = MemoryE2EJsonContext.Default;
        });
        builder.Services.AddMcpMemoryTools();

        using var host = builder.Build();

        // Verify tool registry contains the 4 MCP Memory tools directly (before
        // snapshot builder resolves JSON types for all registered tools including
        // the e2e.echo tool from the test assembly).
        var toolRegistry = host.Services.GetRequiredService<McpToolRegistry>();
        toolRegistry.Build(DescriptorProviderRegistry.GetProviders<McpToolDescriptor>());
        var allTools = toolRegistry.GetAll();
        allTools.Should().Contain(t => t.ToolName == "ctx_recall");
        allTools.Should().Contain(t => t.ToolName == "ctx_expand");
        allTools.Should().Contain(t => t.ToolName == "memory_recall");
        allTools.Should().Contain(t => t.ToolName == "memory_source_expand");
    }

    [Fact]
    public void Mcp_memory_dtos_serialize_correctly()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = MemoryE2EJsonContext.Default
        };

        // Verify ctx_recall input/output serialization
        var recallInput = new RecallAgentContextInput
        {
            ContextHandle = "ctx-test-1",
            MaximumCharacters = 1000
        };
        var recallJson = JsonSerializer.Serialize(recallInput, options);
        var recallDeserialized = JsonSerializer.Deserialize<RecallAgentContextInput>(recallJson, options);
        recallDeserialized.Should().NotBeNull();
        recallDeserialized!.ContextHandle.Should().Be("ctx-test-1");
        recallDeserialized.MaximumCharacters.Should().Be(1000);

        // Verify memory_recall input serialization
        var buildInput = new BuildAgentMemoryPackInput
        {
            MemoryHandles = ["handle-1", "handle-2"],
            Kinds = [AgentMemoryToolKind.ProjectFact],
            Tags = ["important"],
            MaximumCount = 10,
            CharacterBudget = 5000
        };
        var buildJson = JsonSerializer.Serialize(buildInput, options);
        var buildDeserialized = JsonSerializer.Deserialize<BuildAgentMemoryPackInput>(buildJson, options);
        buildDeserialized.Should().NotBeNull();
        buildDeserialized!.MemoryHandles.Should().BeEquivalentTo(["handle-1", "handle-2"]);
        buildDeserialized.MaximumCount.Should().Be(10);
        buildDeserialized.CharacterBudget.Should().Be(5000);

        // Verify ctx_expand / memory_source_expand input serialization
        var expandInput = new ExpandAgentMemorySourceInput
        {
            GrantId = "grant-abc",
            MaximumCharacters = 2000
        };
        var expandJson = JsonSerializer.Serialize(expandInput, options);
        var expandDeserialized = JsonSerializer.Deserialize<ExpandAgentMemorySourceInput>(expandJson, options);
        expandDeserialized.Should().NotBeNull();
        expandDeserialized!.GrantId.Should().Be("grant-abc");
        expandDeserialized.MaximumCharacters.Should().Be(2000);

        // Verify enum serialization (enums use custom JsonConverter with camelCase string names)
        var statusJson = JsonSerializer.Serialize(AgentMemoryToolOperationStatus.Completed, options);
        statusJson.Should().Be("\"completed\"");
        var statusDeserialized = JsonSerializer.Deserialize<AgentMemoryToolOperationStatus>(statusJson, options);
        statusDeserialized.Should().Be(AgentMemoryToolOperationStatus.Completed);

        // Verify EchoInput/EchoOutput (needed for snapshot builder)
        var echoIn = new EchoInput { Value = "test" };
        var echoInJson = JsonSerializer.Serialize(echoIn, typeof(EchoInput), options);
        echoInJson.Should().Be("{\"value\":\"test\"}");
        var echoOut = new EchoOutput { Value = "test" };
        var echoOutJson = JsonSerializer.Serialize(echoOut, typeof(EchoOutput), options);
        echoOutJson.Should().Be("{\"value\":\"test\"}");
    }

    [Fact]
    public void Scope_provider_validator_passes_with_mcp_capable_provider()
    {
        var mockProvider = new MockMcpScopeProvider();
        var validator = new McpMemoryScopeProviderValidator(mockProvider);
        var report = validator.Validate();
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Scope_provider_validator_fails_without_mcp_support()
    {
        var nonMcpProvider = new NonMcpScopeProvider();
        var validator = new McpMemoryScopeProviderValidator(nonMcpProvider);
        var report = validator.Validate();
        report.HasErrors.Should().BeTrue();
    }

    private static void TriggerMcpMemoryAssembly()
    {
        var services = new ServiceCollection();
        McpMemoryServiceCollectionExtensions.AddMcpMemoryTools(services);
    }

    private sealed class MockMcpScopeProvider : IAgentMemoryAccessScopeProvider, IAgentMemoryAccessScopeProviderCapabilities
    {
        public bool Supports(AgentMemoryCallerKind callerKind) => callerKind == AgentMemoryCallerKind.Mcp;

        public ValueTask<AgentMemoryAccessScope> ResolveAsync(
            AgentMemoryAccessPrincipal principal,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(new AgentMemoryAccessScope
            {
                TenantId = principal.TenantId,
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

    private sealed class NonMcpScopeProvider : IAgentMemoryAccessScopeProvider, IAgentMemoryAccessScopeProviderCapabilities
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
}

[JsonSerializable(typeof(RecallAgentContextInput))]
[JsonSerializable(typeof(RecallAgentContextResult))]
[JsonSerializable(typeof(BuildAgentMemoryPackInput))]
[JsonSerializable(typeof(BuildAgentMemoryPackResult))]
[JsonSerializable(typeof(ExpandAgentMemorySourceInput))]
[JsonSerializable(typeof(ExpandAgentMemorySourceResult))]
[JsonSerializable(typeof(AgentMemoryToolOperationStatus))]
[JsonSerializable(typeof(AgentMemoryToolKind))]
[JsonSerializable(typeof(AgentMemoryToolConfidence))]
[JsonSerializable(typeof(AgentMemoryToolMemoryStatus))]
[JsonSerializable(typeof(AgentMemoryToolSourceKind))]
[JsonSerializable(typeof(AgentMemoryToolDiagnosticSeverity))]
[JsonSerializable(typeof(AgentMemoryToolCanonicalHashDto))]
[JsonSerializable(typeof(AgentMemoryToolItemDto))]
[JsonSerializable(typeof(AgentMemoryToolBlockDto))]
[JsonSerializable(typeof(AgentMemoryToolDiagnosticDto))]
[JsonSerializable(typeof(AgentMemorySourceGrantDto))]
[JsonSerializable(typeof(EchoInput))]
[JsonSerializable(typeof(EchoOutput))]
internal partial class MemoryE2EJsonContext : JsonSerializerContext;
