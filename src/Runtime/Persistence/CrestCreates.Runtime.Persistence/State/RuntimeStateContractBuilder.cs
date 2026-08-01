using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Runtime.Persistence.State;

public sealed class RuntimeStateContractBuilder : IRuntimeStateContractBuilder
{
    private readonly List<RuntimeStateRegistration> _registrations = new();

    public void Add<T>(
        string typeId,
        JsonTypeInfo<T> jsonTypeInfo,
        IReadOnlySet<Type> allDirectRootTypes,
        DescriptorRef? schemaRef = null)
    {
        if (string.IsNullOrWhiteSpace(typeId))
            throw new RuntimeStateContractException("Runtime state TypeId is required.");
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ArgumentNullException.ThrowIfNull(allDirectRootTypes);
        if (!allDirectRootTypes.Contains(typeof(T)))
        {
            throw new RuntimeStateContractException(
                $"Generated JSON root manifest does not contain '{typeof(T).FullName}'.");
        }

        _registrations.Add(new TypedRuntimeStateRegistration<T>(typeId, jsonTypeInfo, schemaRef));
    }

    public RuntimeStateContractRegistry Build(ISchemaRefValidator? schemaRefValidator = null)
    {
        var ordered = _registrations
            .OrderBy(x => x.TypeId, StringComparer.Ordinal)
            .ThenBy(x => x.ClrType.FullName, StringComparer.Ordinal)
            .ToArray();

        var duplicateType = ordered.GroupBy(x => x.TypeId, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicateType is not null)
            throw new RuntimeStateContractException($"Duplicate Runtime state TypeId '{duplicateType.Key}'.");

        var duplicateClr = ordered.GroupBy(x => x.ClrType).FirstOrDefault(x => x.Count() > 1);
        if (duplicateClr is not null)
            throw new RuntimeStateContractException(
                $"CLR type '{duplicateClr.Key.FullName}' has multiple Runtime state registrations.");

        var requiresSchemaValidation = ordered.Any(x => x.SchemaRef is not null);
        if (requiresSchemaValidation && schemaRefValidator is null)
        {
            throw new RuntimeStateContractException(
                "Runtime state SchemaRef registrations require an ISchemaRegistry at startup.");
        }

        foreach (var registration in ordered)
            schemaRefValidator?.Validate(registration.SchemaRef);

        return new RuntimeStateContractRegistry(ordered);
    }
}

public interface ISchemaRefValidator
{
    void Validate(DescriptorRef? schemaRef);
}
