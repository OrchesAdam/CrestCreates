using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Mcp.Memory.Tests;

/// <summary>
/// Verifies that when only Mcp.Memory and Agent.Memory.Projection are loaded
/// (without Agent.Memory.Tools), all required schemas and capabilities are available
/// from Projection. This proves the schema ownership migration is complete.
/// </summary>
public class McpMemoryRegistryBuildTests
{
    /// <summary>
    /// In .NET 10, typeof() does not trigger module initialization — actual code
    /// execution from the assembly is required. Calling public service registration
    /// extension methods forces assembly load + module initializer execution for both
    /// Mcp.Memory and Agent.Memory.Projection.
    /// </summary>
    private static void EnsureModulesLoaded()
    {
        var services = new ServiceCollection();
        CrestCreates.Mcp.Memory.McpMemoryServiceCollectionExtensions.AddMcpMemoryTools(services);
        CrestCreates.Agent.Memory.Projection.ProjectionSecurityServiceCollectionExtensions
            .AddAgentMemoryProjectionSecurity(services);
    }

    [Fact]
    public void Schema_registry_builds_all_shared_read_schemas_from_projection()
    {
        EnsureModulesLoaded();

        var engine = new RegistryValidationEngine<SchemaDescriptor>([]);
        var registry = new SchemaRegistry(engine);
        registry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());

        // Verify shared read schemas from Projection
        registry.GetByVersion("canonical-hash", 1).Should().NotBeNull();
        registry.GetByVersion("source-grant", 1).Should().NotBeNull();
        registry.GetByVersion("diagnostic", 1).Should().NotBeNull();
        registry.GetByVersion("block", 1).Should().NotBeNull();
        registry.GetByVersion("item", 1).Should().NotBeNull();
        registry.GetByVersion("build-pack-input", 1).Should().NotBeNull();
        registry.GetByVersion("build-pack-output", 1).Should().NotBeNull();
        registry.GetByVersion("expand-source-input", 1).Should().NotBeNull();
        registry.GetByVersion("expand-source-output", 1).Should().NotBeNull();

        // Verify MCP-specific schemas
        registry.GetByVersion("ctx-recall-input", 1).Should().NotBeNull();
        registry.GetByVersion("ctx-recall-output", 1).Should().NotBeNull();
    }

    [Fact]
    public void Schema_registry_does_not_require_write_specific_schemas_from_tools()
    {
        EnsureModulesLoaded();

        var engine = new RegistryValidationEngine<SchemaDescriptor>([]);
        var registry = new SchemaRegistry(engine);
        registry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());

        // Write-only schemas are NOT in Projection or MCP Memory —
        // they should not be required for read-only MCP scenarios.
        // These would only be available if Agent.Memory.Tools is loaded.
        registry.GetByVersion("candidate", 1).Should().BeNull();
        registry.GetByVersion("compress-history-input", 1).Should().BeNull();
        registry.GetByVersion("compress-history-output", 1).Should().BeNull();
        registry.GetByVersion("extract-candidates-input", 1).Should().BeNull();
        registry.GetByVersion("extract-candidates-output", 1).Should().BeNull();
        registry.GetByVersion("promote-candidate-input", 1).Should().BeNull();
        registry.GetByVersion("promote-candidate-output", 1).Should().BeNull();
        registry.GetByVersion("reject-candidate-input", 1).Should().BeNull();
        registry.GetByVersion("reject-candidate-output", 1).Should().BeNull();
        registry.GetByVersion("supersede-item-input", 1).Should().BeNull();
        registry.GetByVersion("supersede-item-output", 1).Should().BeNull();
    }

    [Fact]
    public void Capability_registry_contains_all_3_mcp_memory_capabilities()
    {
        EnsureModulesLoaded();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

        registry.GetByVersion("mcp.ctx_recall", 1).Should().NotBeNull();
        registry.GetByVersion("mcp-memory:agent.source.expand", 1).Should().NotBeNull();
        registry.GetByVersion("mcp.memory_recall", 1).Should().NotBeNull();
    }

    [Fact]
    public void Mcp_capabilities_reference_schemas_from_projection_not_tools()
    {
        EnsureModulesLoaded();

        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

        var sourceExpand = registry.GetByVersion("mcp-memory:agent.source.expand", 1)!;
        sourceExpand.InputSchema!.Value.Id.Should().Be("expand-source-input");
        sourceExpand.OutputSchema!.Value.Id.Should().Be("expand-source-output");

        var memoryRecall = registry.GetByVersion("mcp.memory_recall", 1)!;
        memoryRecall.InputSchema!.Value.Id.Should().Be("build-pack-input");
        memoryRecall.OutputSchema!.Value.Id.Should().Be("build-pack-output");

        var ctxRecall = registry.GetByVersion("mcp.ctx_recall", 1)!;
        ctxRecall.InputSchema!.Value.Id.Should().Be("ctx-recall-input");
        ctxRecall.OutputSchema!.Value.Id.Should().Be("ctx-recall-output");
    }

    [Fact]
    public void Ctx_recall_output_schema_matches_recall_agent_context_result_dto()
    {
        EnsureModulesLoaded();

        var engine = new RegistryValidationEngine<SchemaDescriptor>([]);
        var registry = new SchemaRegistry(engine);
        registry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());

        var output = registry.GetByVersion("ctx-recall-output", 1)!;

        output.Fields.Should().HaveCount(5);
        output.Fields.Should().Contain(f => f.Name == "OperationStatus" && f.FieldType == "string" && f.IsRequired);
        output.Fields.Should().Contain(f => f.Name == "WasTruncated" && f.FieldType == "bool");
        output.Fields.Should().Contain(f => f.Name == "Blocks" && f.FieldType == "object" && f.IsCollection && f.ObjectSchema!.Value.Id == "block");
        output.Fields.Should().Contain(f => f.Name == "BlockCount" && f.FieldType == "int");
        output.Fields.Should().Contain(f => f.Name == "Diagnostics" && f.FieldType == "object" && f.IsCollection && f.ObjectSchema!.Value.Id == "diagnostic");
    }
}
