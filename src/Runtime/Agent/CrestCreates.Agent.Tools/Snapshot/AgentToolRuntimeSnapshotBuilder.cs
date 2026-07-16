using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolRuntimeSnapshotBuilder
{
    private readonly IAgentToolRegistry _tools;
    private readonly ICapabilityRegistry _capabilities;
    private readonly ISchemaRegistry _schemas;
    private readonly AgentToolCapabilityResolver _capabilityResolver;
    private readonly AgentToolSchemaResolver _schemaResolver;
    private readonly IAgentToolJsonSchemaProjector _projector;
    private readonly AgentToolSchemaParityValidator _parity;
    private readonly AgentToolEffectiveGovernanceDeriver _governance;
    private readonly ICanonicalHashComputer _hashes;
    private readonly AgentToolJsonOptions _json;

    public AgentToolRuntimeSnapshotBuilder(
        IAgentToolRegistry tools,
        ICapabilityRegistry capabilities,
        ISchemaRegistry schemas,
        AgentToolCapabilityResolver capabilityResolver,
        AgentToolSchemaResolver schemaResolver,
        IAgentToolJsonSchemaProjector projector,
        AgentToolSchemaParityValidator parity,
        AgentToolEffectiveGovernanceDeriver governance,
        ICanonicalHashComputer hashes,
        AgentToolJsonOptions json)
    {
        _tools = tools;
        _capabilities = capabilities;
        _schemas = schemas;
        _capabilityResolver = capabilityResolver;
        _schemaResolver = schemaResolver;
        _projector = projector;
        _parity = parity;
        _governance = governance;
        _hashes = hashes;
        _json = json;
    }

    public AgentToolRuntimeSnapshot Build()
    {
        EnsureRegistriesBuilt();

        var allTools = _tools.GetAll();
        ValidateCanonicalHashes(allTools);
        var activeTools = allTools
            .Where(tool => tool.State == DescriptorState.Active)
            .ToArray();
        if (activeTools.Length == 0)
        {
            return new AgentToolRuntimeSnapshot(
                Array.Empty<KeyValuePair<string, AgentToolRuntimeEntry>>()
                    .ToFrozenDictionary(StringComparer.Ordinal));
        }

        var serializerOptions = FreezeJsonOptions(_json.SerializerOptions);
        var entries = activeTools.Select(tool => BuildEntry(tool, serializerOptions)).ToArray();

        try
        {
            return new AgentToolRuntimeSnapshot(entries.ToFrozenDictionary(
                entry => entry.Descriptor.ToolName,
                StringComparer.Ordinal));
        }
        catch (ArgumentException exception)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.ActiveToolNameConflict,
                "Active Agent ToolName is not unique.",
                exception);
        }
    }

    private AgentToolRuntimeEntry BuildEntry(
        AgentCapabilityToolDescriptor tool,
        JsonSerializerOptions serializerOptions)
    {
        var capability = _capabilityResolver.Resolve(tool.Capability);
        var capabilityHash = ContractHash(capability);
        if (tool.Capability.ExpectedContractHash is { } expected
            && !string.Equals(expected, capabilityHash, StringComparison.Ordinal))
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.ExpectedContractHashMismatch,
                "Resolved Capability ContractHash does not match the Agent Tool expectation.");
        }

        var inputSchema = _schemaResolver.Resolve(capability.InputSchema);
        var outputSchema = _schemaResolver.Resolve(capability.OutputSchema);
        var inputSchemaHash = inputSchema is null ? null : ContractHash(inputSchema);
        var outputSchemaHash = outputSchema is null ? null : ContractHash(outputSchema);
        VerifySchemaExpectedHash(capability.InputSchema, inputSchemaHash);
        VerifySchemaExpectedHash(capability.OutputSchema, outputSchemaHash);

        var contract = AgentToolBindingRegistry.Find(tool.Id, tool.Version)
            ?? throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.MissingBinding,
                "Generated Agent Tool binding is missing.");
        ValidateBinding(tool, contract);
        ValidateSchemaTypePresence(inputSchema, contract.InputType, input: true);
        ValidateSchemaTypePresence(outputSchema, contract.OutputType, input: false);

        var inputTypeInfo = ResolveTypeInfo(serializerOptions, contract.InputType);
        var outputTypeInfo = ResolveTypeInfo(serializerOptions, contract.OutputType);
        ValidateTypeInfoRoot(inputTypeInfo);
        ValidateTypeInfoRoot(outputTypeInfo);
        if (inputSchema is not null && inputTypeInfo is not null)
            _parity.ValidateInput(inputSchema, inputTypeInfo);
        if (outputSchema is not null && outputTypeInfo is not null)
            _parity.ValidateOutput(outputSchema, outputTypeInfo);

        var governance = _governance.Derive(tool, capability);
        var toolHash = ContractHash(tool);
        var toolIdentity = new AgentToolContractIdentity(tool.Id, tool.Version, toolHash);
        var capabilityIdentity = new AgentToolContractIdentity(
            capability.Id,
            capability.Version,
            capabilityHash);
        var inputIdentity = BuildSchemaIdentity(inputSchema, inputSchemaHash);
        var outputIdentity = BuildSchemaIdentity(outputSchema, outputSchemaHash);
        var discovery = new AgentToolDiscoveryContract
        {
            ToolName = tool.ToolName,
            Title = tool.Title,
            Description = tool.Description,
            InputSchema = _projector.ProjectInput(inputSchema),
            OutputSchema = _projector.ProjectOutput(outputSchema),
            ToolContract = toolIdentity,
            CapabilityContract = capabilityIdentity,
            InputSchemaContract = inputIdentity,
            OutputSchemaContract = outputIdentity,
            Governance = governance
        };

        return new AgentToolRuntimeEntry(
            tool,
            capability,
            inputSchema,
            outputSchema,
            new AgentToolRuntimeBinding(contract, inputTypeInfo, outputTypeInfo),
            discovery,
            tool.AllowedAgentRoles.ToFrozenSet(StringComparer.Ordinal),
            governance.EffectiveRisk,
            governance.SideEffectKind,
            governance,
            toolHash,
            capabilityHash,
            inputSchemaHash,
            outputSchemaHash);
    }

    private void EnsureRegistriesBuilt()
    {
        if (_tools.State != RegistryState.Built
            || IsKnownNotBuilt(_capabilities)
            || IsKnownNotBuilt(_schemas))
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.SnapshotPublicationFailure,
                "Agent Tool dependency registries must be built before snapshot publication.");
        }
    }

    private void ValidateCanonicalHashes(IReadOnlyList<AgentCapabilityToolDescriptor> tools)
    {
        try
        {
            foreach (var tool in tools)
                _ = ContractHash(tool);
        }
        catch (Exception exception) when (exception is not AgentToolConfigurationException)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.InvalidDescriptorContract,
                "Agent Tool descriptor canonical hash validation failed.",
                exception);
        }
    }

    private static JsonSerializerOptions FreezeJsonOptions(JsonSerializerOptions configured)
    {
        JsonSerializerOptions options;
        try
        {
            options = new JsonSerializerOptions(configured);
        }
        catch (Exception exception)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                "Agent Tool JSON options could not be copied.",
                exception);
        }

        if (options.RespectNullableAnnotations || options.RespectRequiredConstructorParameters)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                "Schema-owned nullability and constructor presence validation must remain disabled.");
        }

        if (options.TypeInfoResolverChain.Count > 0)
        {
            if (options.TypeInfoResolverChain.Any(resolver => resolver is not JsonSerializerContext))
            {
                throw new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                    "Agent Tool JSON resolver chain must contain only source-generated contexts.");
            }
        }
        else if (options.TypeInfoResolver is not JsonSerializerContext)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                "Agent Tool JSON resolver must be a source-generated context.");
        }

        options.MakeReadOnly();
        return options;
    }

    private static JsonTypeInfo? ResolveTypeInfo(JsonSerializerOptions options, Type? type)
    {
        if (type is null)
            return null;

        try
        {
            return options.GetTypeInfo(type);
        }
        catch (NotSupportedException exception)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.MissingJsonTypeInfo,
                "Application-owned source-generated JsonTypeInfo is missing.",
                exception);
        }
    }

    private static void ValidateTypeInfoRoot(JsonTypeInfo? typeInfo)
    {
        if (typeInfo is not null && typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.JsonRootNotObject,
                "Agent Tool JsonTypeInfo root must be an object.");
        }
    }

    private static void ValidateBinding(
        AgentCapabilityToolDescriptor tool,
        AgentToolBindingContract binding)
    {
        var inputRegistered = binding.InputType is null
            || AgentToolJsonContractRegistry.GetInputTypes().Contains(binding.InputType);
        var outputRegistered = binding.OutputType is null
            || AgentToolJsonContractRegistry.GetOutputTypes().Contains(binding.OutputType);
        if (!string.Equals(binding.ToolDescriptorId, tool.Id, StringComparison.Ordinal)
            || binding.ToolDescriptorVersion != tool.Version
            || !inputRegistered
            || !outputRegistered)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.BindingMismatch,
                "Generated Agent Tool binding identity or CLR type registration does not match the descriptor.");
        }
    }

    private static void ValidateSchemaTypePresence(
        SchemaDescriptor? schema,
        Type? type,
        bool input)
    {
        if ((schema is null) != (type is null))
        {
            throw new AgentToolConfigurationException(
                input
                    ? AgentToolStartupDiagnosticCodes.InputSchemaTypeMismatch
                    : AgentToolStartupDiagnosticCodes.OutputSchemaTypeMismatch,
                $"Agent Tool {(input ? "input" : "output")} Schema and CLR type must both be present or absent.");
        }
    }

    private static AgentToolSchemaContractIdentity? BuildSchemaIdentity(
        SchemaDescriptor? schema,
        string? hash)
        => schema is null || hash is null
            ? null
            : new AgentToolSchemaContractIdentity(schema.Id, schema.Version, hash);

    private static void VerifySchemaExpectedHash(
        VersionedDescriptorRef<SchemaDescriptor>? reference,
        string? actualHash)
    {
        if (reference?.ExpectedContractHash is { } expected
            && !string.Equals(expected, actualHash, StringComparison.Ordinal))
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.ExpectedContractHashMismatch,
                "Resolved Schema ContractHash does not match the Capability reference expectation.");
        }
    }

    private static bool IsKnownNotBuilt(object registry)
        => registry is IRegistryState state && state.State != RegistryState.Built;

    private string ContractHash(IDescriptor descriptor)
        => _hashes.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull).Value;
}
