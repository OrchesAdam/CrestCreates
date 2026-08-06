using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Tools.Persistence.Testing.Drivers;
using CrestCreates.Agent.Tools.Persistence.Testing.Fixtures;

namespace CrestCreates.Agent.Tools.Persistence.Testing;

public abstract class AgentToolPreDispatchContractTestBase<TFixture>
    where TFixture : AgentToolPreDispatchContractFixture
{
    protected AgentToolPreDispatchContractTestBase(TFixture fixture)
    {
        Fixture = fixture;
    }

    protected TFixture Fixture { get; }

    protected abstract IAgentToolPreDispatchContractDriver CreateDriver();
}
