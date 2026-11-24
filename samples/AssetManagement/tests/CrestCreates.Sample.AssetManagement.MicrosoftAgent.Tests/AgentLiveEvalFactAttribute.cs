using Xunit;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

public sealed class AgentLiveEvalFactAttribute : FactAttribute
{
    public AgentLiveEvalFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CREST_AGENT_LIVE_EVAL"), "1", StringComparison.Ordinal))
        {
            Skip = "Set CREST_AGENT_LIVE_EVAL=1 and DEEPSEEK_API_KEY to run paid real-model coordination evaluations.";
        }
    }
}
