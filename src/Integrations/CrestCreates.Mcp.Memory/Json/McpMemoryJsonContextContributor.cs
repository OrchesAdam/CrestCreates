using System.Text.Json;
using CrestCreates.Mcp.Abstractions;

namespace CrestCreates.Mcp.Memory.Json;

internal sealed class McpMemoryJsonContextContributor : IMcpToolJsonContextContributor
{
    public string ContributorId => "mcp-memory";

    public void Contribute(McpJsonContextBuilder builder, JsonSerializerOptions options)
    {
        // JsonSerializerContext constructor adds the context to TypeInfoResolverChain
        // via AddContext, which also freezes the options. Do NOT call
        // TypeInfoResolverChain.Add again — it would fail with "options cannot be
        // modified once encapsulated by a JsonSerializerContext."
        var context = new McpMemoryJsonSerializerContext(options);

        // ctx_recall
        builder.AddBinding("ctx_recall_input", context.RecallAgentContextInput, ContributorId);
        builder.AddBinding("ctx_recall_output", context.RecallAgentContextResult, ContributorId);

        // memory_recall
        builder.AddBinding("memory_recall_input", context.BuildAgentMemoryPackInput, ContributorId);
        builder.AddBinding("memory_recall_output", context.BuildAgentMemoryPackResult, ContributorId);

        // ctx_expand / memory_source_expand
        builder.AddBinding("ctx_expand_input", context.ExpandAgentMemorySourceInput, ContributorId);
        builder.AddBinding("ctx_expand_output", context.ExpandAgentMemorySourceResult, ContributorId);
        builder.AddBinding("memory_source_expand_input", context.ExpandAgentMemorySourceInput, ContributorId);
        builder.AddBinding("memory_source_expand_output", context.ExpandAgentMemorySourceResult, ContributorId);
    }
}
