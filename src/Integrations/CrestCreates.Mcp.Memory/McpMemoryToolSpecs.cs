using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Mcp;

namespace CrestCreates.Mcp.Memory;

[McpToolSpecs]
public static partial class McpMemoryTools
{
    [McpToolSpec(
        "mcp.ctx_recall",
        InputType = typeof(RecallAgentContextInput),
        OutputType = typeof(RecallAgentContextResult),
        ToolName = "ctx_recall",
        Title = "Context Recall",
        Description = "Recalls context content from a previously issued context handle.",
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class CtxRecall { }

    [McpToolSpec(
        "mcp.ctx_expand",
        InputType = typeof(ExpandAgentMemorySourceInput),
        OutputType = typeof(ExpandAgentMemorySourceResult),
        ToolName = "ctx_expand",
        Title = "Context Expand",
        Description = "Expands raw source content from a previously issued source grant. Zero artifact writes.",
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class CtxExpand { }

    [McpToolSpec(
        "mcp.memory_recall",
        InputType = typeof(BuildAgentMemoryPackInput),
        OutputType = typeof(BuildAgentMemoryPackResult),
        ToolName = "memory_recall",
        Title = "Memory Recall",
        Description = "Recalls memory items matching the input query. May issue resource handles and source grants.",
        IdempotentHint = McpBooleanHint.False,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class MemoryRecall { }

    [McpToolSpec(
        "mcp.memory_source_expand",
        InputType = typeof(ExpandAgentMemorySourceInput),
        OutputType = typeof(ExpandAgentMemorySourceResult),
        ToolName = "memory_source_expand",
        Title = "Memory Source Expand",
        Description = "Expands raw source content from a previously issued source grant. Zero artifact writes.",
        IdempotentHint = McpBooleanHint.True,
        OpenWorldHint = McpBooleanHint.False)]
    public sealed class MemorySourceExpand { }
}
