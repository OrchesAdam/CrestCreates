using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Tools;

namespace CrestCreates.Agent.Memory.Tools;

internal sealed class AgentMemoryToolJsonContextContributor : IAgentToolJsonContextContributor
{
    private static readonly Type[] Roots =
    [
        typeof(BuildAgentMemoryPackInput), typeof(ExpandAgentMemorySourceInput),
        typeof(CompressAgentHistoryInput), typeof(ExtractMemoryCandidatesInput),
        typeof(PromoteMemoryCandidateInput), typeof(RejectMemoryCandidateInput),
        typeof(SupersedeMemoryItemInput), typeof(BuildAgentMemoryPackResult),
        typeof(ExpandAgentMemorySourceResult), typeof(CompressAgentHistoryResult),
        typeof(ExtractMemoryCandidatesResult), typeof(PromoteMemoryCandidateResult),
        typeof(RejectMemoryCandidateResult), typeof(SupersedeMemoryItemResult)
    ];

    public string Id => "agent-memory-tools";
    public int Order => 200;
    public string ModuleId => "agent-memory-tools";
    public IReadOnlyCollection<Type> BindingRootTypes => Roots;

    public JsonSerializerContext Create(JsonSerializerOptions sharedOptions)
        => new AgentMemoryToolJsonSerializerContext(sharedOptions);
}
