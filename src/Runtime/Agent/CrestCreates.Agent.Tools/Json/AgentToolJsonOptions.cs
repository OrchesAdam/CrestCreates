using System.Text.Json;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolJsonOptions
{
    public JsonSerializerOptions SerializerOptions { get; } = new();

    public IList<IAgentToolJsonContextContributor> ContextContributors { get; } =
        new List<IAgentToolJsonContextContributor>();

    public ISet<string> EnabledModuleIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "default"
    };
}
