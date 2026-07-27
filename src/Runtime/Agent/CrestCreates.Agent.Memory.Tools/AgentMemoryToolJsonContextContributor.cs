using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Agent.Tools;

namespace CrestCreates.Agent.Memory.Tools;

internal sealed class AgentMemoryToolJsonContextContributor : IAgentToolJsonContextContributor
{
    public string Id => "agent-memory-tools";
    public int Order => 200;
    public string ModuleId => "agent-memory-tools";
    public IReadOnlyCollection<Type> BindingRootTypes =>
        AgentMemoryToolJsonSerializerContext.AgentMemoryToolJsonSerializerContextRootManifest.BindingRootTypes;

    public JsonSerializerContext Create(JsonSerializerOptions sharedOptions)
        => new AgentMemoryToolJsonSerializerContext(sharedOptions);
}
