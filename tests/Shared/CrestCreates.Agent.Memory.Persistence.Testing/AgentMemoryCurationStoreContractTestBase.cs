using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using CrestCreates.Agent.Memory.Persistence.Testing.Fixtures;

namespace CrestCreates.Agent.Memory.Persistence.Testing;

/// <summary>
/// Runner-free base for Agent Memory curation contract runners. Concrete xUnit
/// runner classes derive from this base, expose the exact Spec §18.1 curation
/// skeleton method names as tests, and delegate to the shared
/// <c>AgentMemoryCurationStoreContractCases</c> methods with a real driver.
/// </summary>
public abstract class AgentMemoryCurationStoreContractTestBase<TFixture> : AgentMemoryStoreContractTestBase<TFixture>
    where TFixture : AgentMemoryPersistenceContractFixture
{
    protected AgentMemoryCurationStoreContractTestBase(TFixture fixture)
        : base(fixture)
    {
    }
}
