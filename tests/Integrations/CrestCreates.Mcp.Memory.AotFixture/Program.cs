using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp;
using CrestCreates.Mcp.Memory;
using CrestCreates.Mcp.Memory.AotFixture;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

return await McpMemoryAotFixtureRunner.RunAsync();

internal static class McpMemoryAotFixtureRunner
{
    public static async Task<int> RunAsync()
    {
        try
        {
            // Trigger MCP Memory assembly load (module init registers providers).
            // Calling AddMcpMemoryTools on a dummy collection is sufficient.
            var dummy = new ServiceCollection();
            McpMemoryServiceCollectionExtensions.AddMcpMemoryTools(dummy);

            // Build registries from providers.
            var schemas = new SchemaRegistry(new RegistryValidationEngine<SchemaDescriptor>([]));
            schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
            var capabilities = new CapabilityRegistry(new RegistryValidationEngine<CapabilityDescriptor>([]));
            capabilities.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());

            // Build host with MCP tool projection + MCP Memory tools.
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton<ISchemaRegistry>(schemas);
            builder.Services.AddSingleton<ICapabilityRegistry>(capabilities);
            builder.Services.AddSingleton<IAgentMemoryAccessScopeProvider>(new AotScopeProvider());
            builder.Services.AddAgentMemoryRuntime();
            builder.Services.AddCapabilityRuntime();
            builder.Services.AddCrestMcpToolProjection(options =>
            {
                options.SerializerOptions.TypeInfoResolver = McpMemoryAotJsonContext.Default;
            });
            builder.Services.AddMcpMemoryTools();

            using var host = builder.Build();
            await host.StartAsync();

            // Verify the 4 MCP Memory tools are in the snapshot.
            var snapshot = host.Services.GetRequiredService<McpToolRuntimeSnapshotProvider>().GetRequired();
            if (snapshot.Find("ctx_recall") is null) return 2;
            if (snapshot.Find("ctx_expand") is null) return 3;
            if (snapshot.Find("memory_recall") is null) return 4;
            if (snapshot.Find("memory_source_expand") is null) return 5;

            // Verify JSON context resolves MCP Memory DTO types.
            var options = new JsonSerializerOptions(McpMemoryAotJsonContext.Default.Options);
            options.TypeInfoResolver = McpMemoryAotJsonContext.Default;
            if (options.GetTypeInfo(typeof(RecallAgentContextInput)) is null) return 6;
            if (options.GetTypeInfo(typeof(BuildAgentMemoryPackInput)) is null) return 7;
            if (options.GetTypeInfo(typeof(ExpandAgentMemorySourceInput)) is null) return 8;

            Console.WriteLine("MCP_MEMORY_NATIVEAOT_PIPELINE_OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
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
}
