using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Json;

namespace CrestCreates.Runtime.Persistence.State;

public sealed class BuiltInRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
    {
        builder.Add(
            "crest.runtime/string/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.String,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
        builder.Add(
            "crest.runtime/boolean/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.Boolean,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
        builder.Add(
            "crest.runtime/int32/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.Int32,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
        builder.Add(
            "crest.runtime/int64/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.Int64,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
        builder.Add(
            "crest.runtime/decimal/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.Decimal,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
        builder.Add(
            "crest.runtime/guid/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.Guid,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
        builder.Add(
            "crest.runtime/date-time-offset/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.DateTimeOffset,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
        builder.Add(
            "crest.runtime/state-bag/v1",
            BuiltInRuntimeStateJsonSerializerContext.Default.RuntimeStateBag,
            BuiltInRuntimeStateJsonSerializerContext.BuiltInRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes);
    }
}
