using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Shared exact output preflight used by generated binding roots. It is kept
/// generic so generated modules can register a closed implementation without
/// reflection or a second Schema/JSON validation path.
/// </summary>
public sealed class AgentToolOutputPreflight<TOutput> : IAgentToolOutputPreflight<TOutput>
{
    private readonly string _descriptorId;
    private readonly int _descriptorVersion;
    private readonly string _contractFingerprint;
    private readonly JsonTypeInfo<TOutput> _typeInfo;
    private readonly SchemaDescriptor? _schema;
    private readonly IReadOnlyList<SchemaDescriptor> _referencedSchemas;
    private readonly ISchemaValidator _validator;
    private readonly Func<TOutput, IReadOnlyList<AgentToolAuditFact>> _facts;

    public AgentToolOutputPreflight(
        string descriptorId,
        int descriptorVersion,
        string contractFingerprint,
        JsonTypeInfo<TOutput> typeInfo,
        SchemaDescriptor? schema,
        IReadOnlyList<SchemaDescriptor>? referencedSchemas,
        ISchemaValidator validator,
        Func<TOutput, IReadOnlyList<AgentToolAuditFact>>? facts = null)
    {
        _descriptorId = descriptorId;
        _descriptorVersion = descriptorVersion;
        _contractFingerprint = contractFingerprint;
        _typeInfo = typeInfo;
        _schema = schema;
        _referencedSchemas = referencedSchemas ?? Array.Empty<SchemaDescriptor>();
        _validator = validator;
        _facts = facts ?? (_ => Array.Empty<AgentToolAuditFact>());
    }

    public AgentToolPreparedOutput<TOutput> Prepare(TOutput output)
    {
        var structured = JsonSerializer.SerializeToElement(output, _typeInfo);
        if (_schema is not null
            && !_validator.Validate(_schema, structured, _referencedSchemas, rejectUnknownProperties: true).IsValid)
            throw new InvalidOperationException("Agent Tool output failed exact Schema preflight.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(structured.GetRawText()))).ToLowerInvariant();
        return new AgentToolPreparedOutput<TOutput>
        {
            Output = output,
            StructuredOutput = structured.Clone(),
            ProjectedOutputFacts = _facts(output),
            Receipt = new AgentToolOutputPreflightReceipt
            {
                ToolDescriptorId = _descriptorId,
                ToolDescriptorVersion = _descriptorVersion,
                OutputContractFingerprint = _contractFingerprint,
                StructuredOutputHash = hash
            }
        };
    }
}
