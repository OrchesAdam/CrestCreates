using Xunit;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgentLiveEvaluationCollection
{
    public const string Name = "Agent live evaluation";
}
