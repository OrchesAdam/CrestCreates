using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Runtime.Persistence.Abstractions.State;

public interface IRuntimeStateContractBuilder
{
    void Add<T>(
        string typeId,
        JsonTypeInfo<T> jsonTypeInfo,
        IReadOnlySet<Type> allDirectRootTypes,
        DescriptorRef? schemaRef = null);
}
