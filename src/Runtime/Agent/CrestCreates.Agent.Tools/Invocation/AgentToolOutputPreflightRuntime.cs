using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

internal sealed class AgentToolOutputPreflightRuntime : IAgentToolOutputPreflightRuntime
{
    private readonly string _descriptorId;
    private readonly int _descriptorVersion;
    private readonly string _contractFingerprint;
    private readonly Type _outputType;
    private readonly JsonTypeInfo _outputTypeInfo;
    private readonly SchemaDescriptor? _schema;
    private readonly IReadOnlyList<SchemaDescriptor> _references;
    private readonly ISchemaValidator _validator;
    private readonly Func<object?, IReadOnlyList<AgentToolAuditFact>>? _auditProjector;
    private readonly AgentToolAuditProjectionContract? _auditContract;

    public AgentToolOutputPreflightRuntime(
        string descriptorId,
        int descriptorVersion,
        string contractFingerprint,
        Type outputType,
        JsonTypeInfo outputTypeInfo,
        SchemaDescriptor? schema,
        IReadOnlyList<SchemaDescriptor> references,
        ISchemaValidator validator,
        Func<object?, IReadOnlyList<AgentToolAuditFact>>? auditProjector = null,
        AgentToolAuditProjectionContract? auditContract = null)
    {
        _descriptorId = descriptorId;
        _descriptorVersion = descriptorVersion;
        _contractFingerprint = contractFingerprint;
        _outputType = outputType;
        _outputTypeInfo = outputTypeInfo;
        _schema = schema;
        _references = references;
        _validator = validator;
        _auditProjector = auditProjector;
        _auditContract = auditContract;
    }

    public AgentToolPreparedOutput<TOutput> Prepare<TOutput>(TOutput output)
    {
        if (output is null || typeof(TOutput) != _outputType || _outputTypeInfo is not JsonTypeInfo<TOutput> typeInfo)
            throw new InvalidOperationException("Prepared output type does not match the frozen binding root.");
        var prepared = new AgentToolOutputPreflight<TOutput>(
            _descriptorId, _descriptorVersion, _contractFingerprint, typeInfo,
            _schema, _references, _validator,
            _auditProjector is null ? null : value => _auditProjector(value)).Prepare(output);
        if (!AgentToolAuditFactValidator.Validate(
                prepared.ProjectedOutputFacts,
                Math.Min(64, _auditContract?.MaximumFacts ?? 64),
                _auditContract))
            throw new InvalidOperationException("Prepared output audit facts violate the frozen Tool contract.");
        return prepared;
    }
}
