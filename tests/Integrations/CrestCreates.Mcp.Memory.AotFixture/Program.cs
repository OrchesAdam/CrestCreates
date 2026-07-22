using System.Text.Json;
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

            // Invoke all 4 MCP Memory tools via IMcpToolInvoker.
            // Uses a scope since IMcpToolInvoker and pipeline services are scoped.
            await using var scope = host.Services.CreateAsyncScope();
            var invoker = scope.ServiceProvider.GetRequiredService<IMcpToolInvoker>();

            var hostContext = new McpToolHostContext("aot-fixture", "Production");
            var allSucceeded = true;

            var ctxRecallResult = await InvokeSafeAsync(invoker, hostContext,
                "ctx_recall", new RecallAgentContextInput { ContextHandle = "test-handle", MaximumCharacters = 100 });
            if (ctxRecallResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: ctx_recall (error={ctxRecallResult.ErrorCode})");
                allSucceeded = false;
            }
            else
                Console.WriteLine("ctx_recall: OK");

            var ctxExpandResult = await InvokeSafeAsync(invoker, hostContext,
                "ctx_expand", new ExpandAgentMemorySourceInput { GrantId = "test-grant", MaximumCharacters = 100 });
            if (ctxExpandResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: ctx_expand (error={ctxExpandResult.ErrorCode})");
                allSucceeded = false;
            }
            else
                Console.WriteLine("ctx_expand: OK");

            var memoryRecallResult = await InvokeSafeAsync(invoker, hostContext,
                "memory_recall", new BuildAgentMemoryPackInput { MaximumCount = 10, CharacterBudget = 1000 });
            if (memoryRecallResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: memory_recall (error={memoryRecallResult.ErrorCode})");
                allSucceeded = false;
            }
            else
                Console.WriteLine("memory_recall: OK");

            var memoryExpandResult = await InvokeSafeAsync(invoker, hostContext,
                "memory_source_expand", new ExpandAgentMemorySourceInput { GrantId = "test-grant", MaximumCharacters = 100 });
            if (memoryExpandResult.IsError)
            {
                Console.Error.WriteLine($"FAIL: memory_source_expand (error={memoryExpandResult.ErrorCode})");
                allSucceeded = false;
            }
            else
                Console.WriteLine("memory_source_expand: OK");

            if (!allSucceeded)
            {
                Console.Error.WriteLine("AOT_FIXTURE_FAILED");
                return 1;
            }

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

    private static async Task<McpToolInvocationOutcome> InvokeSafeAsync(
        IMcpToolInvoker invoker,
        McpToolHostContext hostContext,
        string toolName,
        object input)
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(input, input.GetType(), McpMemoryAotJsonContext.Default);
            var callContext = new McpToolCallContext(
                hostContext,
                InvocationId: $"inv-{toolName}",
                RequestId: $"req-{toolName}",
                SessionId: $"session-{toolName}");
            return await invoker.InvokeAsync(toolName, json, callContext, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Tool invocation failed — expected when running without full host context.
            // The pipeline is still validated: tool resolution, JSON binding,
            // and capability dispatch all execute via AOT-compatible code paths.
            // The handler fails on missing TenantId/UserId in the execution context.
            Console.Error.WriteLine($"[TRACE] {toolName}: {ex.GetType().Name}: {ex.Message}");
            return new McpToolInvocationOutcome(
                IsError: true,
                Content: new List<McpToolContent>(),
                ErrorCode: ex.GetType().Name);
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
