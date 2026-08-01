using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.Runtime.Persistence.Tests.Fixtures;

namespace CrestCreates.Runtime.Persistence.Tests.Json;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonContractSurface(typeof(ITestRuntimeStateJsonSurface))]
[JsonContractExplicitRoot(typeof(MutableNestedRuntimeState))]
public sealed partial class TestRuntimeStateJsonSerializerContext : JsonSerializerContext
{
}

internal interface ITestRuntimeStateJsonSurface
{
    MutableNestedRuntimeState State(MutableNestedRuntimeState value);
}
