using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Runtime.Persistence.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(typeof(IRuntimeStateJsonContractSurface))]
[JsonContractExplicitRoot(typeof(string))]
[JsonContractExplicitRoot(typeof(bool))]
[JsonContractExplicitRoot(typeof(int))]
[JsonContractExplicitRoot(typeof(long))]
[JsonContractExplicitRoot(typeof(decimal))]
[JsonContractExplicitRoot(typeof(Guid))]
[JsonContractExplicitRoot(typeof(DateTimeOffset))]
[JsonContractExplicitRoot(typeof(RuntimeStateBag))]
[JsonContractExplicitRoot(typeof(RuntimeStateValue))]
public sealed partial class BuiltInRuntimeStateJsonSerializerContext : JsonSerializerContext
{
}

internal interface IRuntimeStateJsonContractSurface
{
    string String(string value);
    bool Boolean(bool value);
    int Int32(int value);
    long Int64(long value);
    decimal Decimal(decimal value);
    Guid Guid(Guid value);
    DateTimeOffset DateTimeOffset(DateTimeOffset value);
    RuntimeStateBag StateBag(RuntimeStateBag value);
    RuntimeStateValue StateValue(RuntimeStateValue value);
}
