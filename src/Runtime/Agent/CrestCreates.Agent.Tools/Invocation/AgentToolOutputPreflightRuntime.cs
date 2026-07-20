using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

internal sealed class AgentToolOutputPreflightRuntime : IAgentToolOutputPreflightRuntime
{
    private readonly string _descriptorId;
    private readonly int _descriptorVersion;
    private readonly string _contractFingerprint;
    private readonly SchemaDescriptor? _schema;
    private readonly IReadOnlyList<SchemaDescriptor> _references;
    private readonly ISchemaValidator _validator;

    public AgentToolOutputPreflightRuntime(
        string descriptorId,
        int descriptorVersion,
        string contractFingerprint,
        SchemaDescriptor? schema,
        IReadOnlyList<SchemaDescriptor> references,
        ISchemaValidator validator)
    {
        _descriptorId = descriptorId;
        _descriptorVersion = descriptorVersion;
        _contractFingerprint = contractFingerprint;
        _schema = schema;
        _references = references;
        _validator = validator;
    }

    public AgentToolPreparedOutput<TOutput> Prepare<TOutput>(TOutput output, JsonTypeInfo<TOutput> typeInfo)
        => new AgentToolOutputPreflight<TOutput>(
            _descriptorId, _descriptorVersion, _contractFingerprint, typeInfo,
            _schema, _references, _validator).Prepare(output);
}
