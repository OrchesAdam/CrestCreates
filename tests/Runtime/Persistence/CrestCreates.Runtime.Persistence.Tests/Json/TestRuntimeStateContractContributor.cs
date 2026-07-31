using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Tests.Fixtures;

namespace CrestCreates.Runtime.Persistence.Tests.Json;

public sealed class TestRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
        => builder.Add(
            "test/runtime/mutable-state/v1",
            TestRuntimeStateJsonSerializerContext.Default.MutableNestedRuntimeState,
            TestRuntimeStateJsonSerializerContext.TestRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
}
