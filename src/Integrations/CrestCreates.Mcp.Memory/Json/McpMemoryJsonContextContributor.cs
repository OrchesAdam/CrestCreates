using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Mcp.Abstractions;

namespace CrestCreates.Mcp.Memory.Json;

internal sealed class McpMemoryJsonContextContributor : IMcpToolJsonContextContributor
{
    public string ContributorId => "mcp-memory";

    public IReadOnlySet<Type> BindingRootTypes => _bindingRootTypes;

    private static readonly IReadOnlySet<Type> _bindingRootTypes = new HashSet<Type>
    {
        typeof(RecallAgentContextInput),
        typeof(RecallAgentContextResult),
        typeof(BuildAgentMemoryPackInput),
        typeof(BuildAgentMemoryPackResult),
        typeof(ExpandAgentMemorySourceInput),
        typeof(ExpandAgentMemorySourceResult),
    };

    public void Contribute(McpJsonContextBuilder builder)
    {
        var context = new McpMemoryJsonSerializerContext(new JsonSerializerOptions());

        builder.AddBinding("ctx_recall_input", context.RecallAgentContextInput, ContributorId);
        builder.AddBinding("ctx_recall_output", context.RecallAgentContextResult, ContributorId);

        builder.AddBinding("memory_recall_input", context.BuildAgentMemoryPackInput, ContributorId);
        builder.AddBinding("memory_recall_output", context.BuildAgentMemoryPackResult, ContributorId);

        builder.AddBinding("ctx_expand_input", context.ExpandAgentMemorySourceInput, ContributorId);
        builder.AddBinding("ctx_expand_output", context.ExpandAgentMemorySourceResult, ContributorId);
        builder.AddBinding("memory_source_expand_input", context.ExpandAgentMemorySourceInput, ContributorId);
        builder.AddBinding("memory_source_expand_output", context.ExpandAgentMemorySourceResult, ContributorId);

        foreach (var rootType in _bindingRootTypes)
            builder.AddBindingRootOwnership(rootType, ContributorId);
    }

    public JsonSerializerContext CreateContext()
        => new McpMemoryJsonSerializerContext(new JsonSerializerOptions());
}
