using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public sealed class McpToolRuntimeSnapshotBuilder
{
    private readonly IMcpToolRegistry _tools;
    private readonly ICapabilityRegistry _capabilities;
    private readonly ISchemaRegistry _schemas;
    private readonly IMcpJsonSchemaProjector _projector;
    private readonly McpToolSchemaParityValidator _parity;
    private readonly ICanonicalHashComputer _hashes;
    private readonly McpJsonOptions _json;

    public McpToolRuntimeSnapshotBuilder(
        IMcpToolRegistry tools,
        ICapabilityRegistry capabilities,
        ISchemaRegistry schemas,
        IMcpJsonSchemaProjector projector,
        McpToolSchemaParityValidator parity,
        ICanonicalHashComputer hashes,
        McpJsonOptions json)
    {
        _tools = tools;
        _capabilities = capabilities;
        _schemas = schemas;
        _projector = projector;
        _parity = parity;
        _hashes = hashes;
        _json = json;
    }

    public McpToolRuntimeSnapshot Build()
    {
        if (_tools.State != RegistryState.Built
            || IsKnownNotBuilt(_capabilities)
            || IsKnownNotBuilt(_schemas))
        {
            throw new McpToolConfigurationException(
                "MCP_SNAPSHOT_NOT_READY",
                "MCP dependency registries must be built before the runtime snapshot.");
        }
        var serializerOptions = new JsonSerializerOptions(_json.SerializerOptions);
        ValidateResolvers(serializerOptions);
        ValidateInputConstraintOptions(serializerOptions);
        serializerOptions.MakeReadOnly();

        var entries = new List<McpToolRuntimeEntry>();
        foreach (var tool in _tools.GetAll().Where(tool => tool.State == DescriptorState.Active))
        {
            var capability = ResolveCapability(tool.Capability);
            var inputSchema = ResolveSchema(capability.InputSchema);
            var outputSchema = ResolveSchema(capability.OutputSchema);
            var binding = McpToolBindingRegistry.Find(tool.Id, tool.Version)
                ?? throw new McpToolConfigurationException("MCP110", "Generated MCP binding is missing.");
            var inputTypeInfo = ResolveTypeInfo(serializerOptions, binding.InputType, "MCP111");
            var outputTypeInfo = ResolveTypeInfo(serializerOptions, binding.OutputType, "MCP111");

            ValidateSchemaTypePresence(inputSchema, binding.InputType, "input");
            ValidateSchemaTypePresence(outputSchema, binding.OutputType, "output");
            if (inputSchema is not null && inputTypeInfo is not null)
                _parity.ValidateInput(inputSchema, inputTypeInfo);
            if (outputSchema is not null && outputTypeInfo is not null)
                _parity.ValidateOutput(outputSchema, outputTypeInfo);

            var annotations = BuildAnnotations(capability, tool.AnnotationOverrides);
            var contract = new McpToolContract(
                tool.ToolName,
                tool.Title,
                tool.Description,
                _projector.ProjectInput(inputSchema),
                _projector.ProjectOutput(outputSchema),
                annotations);
            entries.Add(new McpToolRuntimeEntry(
                tool,
                capability,
                inputSchema,
                outputSchema,
                new McpToolRuntimeBinding(binding, inputTypeInfo, outputTypeInfo),
                contract,
                ContractHash(tool),
                ContractHash(capability),
                inputSchema is null ? null : ContractHash(inputSchema),
                outputSchema is null ? null : ContractHash(outputSchema)));
        }

        try
        {
            return new McpToolRuntimeSnapshot(entries.ToFrozenDictionary(
                entry => entry.Descriptor.ToolName,
                StringComparer.Ordinal));
        }
        catch (ArgumentException)
        {
            throw new McpToolConfigurationException("MCP102", "Active MCP ToolName is not unique.");
        }
    }

    private CapabilityDescriptor ResolveCapability(CapabilityProjectionReference reference)
    {
        var capability = reference.SelectionMode switch
        {
            VersionSelectionMode.Exact when reference.Version > 0 => _capabilities.GetByVersion(reference.Id, reference.Version),
            VersionSelectionMode.Latest when reference.Version == 0 => _capabilities.GetAll()
                .Where(item => item.Id == reference.Id && item.State == DescriptorState.Active)
                .MaxBy(item => item.Version),
            _ => throw new McpToolConfigurationException("MCP117", "Capability selection is unsupported.")
        };
        return capability ?? throw new McpToolConfigurationException("MCP103", "Capability could not be resolved.");
    }

    private SchemaDescriptor? ResolveSchema(VersionedDescriptorRef<SchemaDescriptor>? reference)
    {
        if (reference is null)
            return null;
        if (reference.Value.SelectionMode != VersionSelectionMode.Exact || reference.Value.Version <= 0)
            throw new McpToolConfigurationException("MCP118", "Capability Schema reference must be exact.");
        if (reference.Value.ExpectedContractHash is not null)
            throw new McpToolConfigurationException("MCP119", "Capability Schema ExpectedContractHash is unsupported.");
        return _schemas.GetByVersion(reference.Value.Id, reference.Value.Version)
            ?? throw new McpToolConfigurationException("MCP104", "Schema could not be resolved.");
    }

    private static JsonTypeInfo? ResolveTypeInfo(JsonSerializerOptions options, Type? type, string code)
    {
        if (type is null)
            return null;
        try
        {
            return options.GetTypeInfo(type);
        }
        catch (NotSupportedException)
        {
            throw new McpToolConfigurationException(code, "Application JsonTypeInfo is missing.");
        }
    }

    private static bool IsKnownNotBuilt(object registry)
        => registry is IRegistryState state
            && state.State != RegistryState.Built;

    private static void ValidateResolvers(JsonSerializerOptions options)
    {
        if (options.TypeInfoResolverChain.Count > 0)
        {
            if (options.TypeInfoResolverChain.Any(resolver => resolver is not JsonSerializerContext))
                throw new McpToolConfigurationException("MCP114", "MCP JSON resolver chain must contain only source-generated contexts.");
            return;
        }

        if (options.TypeInfoResolver is not JsonSerializerContext)
            throw new McpToolConfigurationException("MCP114", "MCP JSON resolver must be source-generated.");
    }

    private static void ValidateInputConstraintOptions(JsonSerializerOptions options)
    {
        if (options.RespectNullableAnnotations || options.RespectRequiredConstructorParameters)
        {
            throw new McpToolConfigurationException(
                "MCP114",
                "MCP JSON options must leave Schema-owned nullability and constructor presence validation to the Capability Pipeline.");
        }
    }

    private static void ValidateSchemaTypePresence(SchemaDescriptor? schema, Type? type, string direction)
    {
        if ((schema is null) != (type is null))
            throw new McpToolConfigurationException("MCP108", $"MCP {direction} Schema and CLR type must both be present or absent.");
    }

    private static McpToolAnnotations BuildAnnotations(
        CapabilityDescriptor capability,
        McpToolAnnotationOverrides overrides)
    {
        var readOnly = capability.CapabilityKind == CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityKind.Query;
        return new McpToolAnnotations(
            readOnly,
            overrides.DestructiveHint,
            overrides.IdempotentHint,
            overrides.OpenWorldHint);
    }

    private string ContractHash<T>(T descriptor) where T : class, IDescriptor
        => _hashes.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull).Value;
}
