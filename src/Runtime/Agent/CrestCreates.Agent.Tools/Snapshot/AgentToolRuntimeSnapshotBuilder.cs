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
    private readonly IReadOnlyList<IAgentToolPreparedOutcomeRequirementProvider> _preparedOutcomeProviders;

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
        AgentToolJsonOptions json,
        IEnumerable<IAgentToolPreparedOutcomeRequirementProvider>? preparedOutcomeProviders = null)
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
        _preparedOutcomeProviders = preparedOutcomeProviders?.ToArray() ?? Array.Empty<IAgentToolPreparedOutcomeRequirementProvider>();
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

        var jsonSetup = FreezeJsonOptions(_json);
        var entries = activeTools.Select(tool => BuildEntry(tool, jsonSetup.Options, jsonSetup.Contexts)).ToArray();

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
        JsonSerializerOptions serializerOptions,
        IReadOnlyList<JsonSerializerContext> contributorContexts)
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

        var inputTypeInfo = ResolveTypeInfo(serializerOptions, contract.InputType, contributorContexts);
        var outputTypeInfo = ResolveTypeInfo(serializerOptions, contract.OutputType, contributorContexts);
        ValidateTypeInfoRoot(inputTypeInfo);
        ValidateTypeInfoRoot(outputTypeInfo);
        if (inputSchema is not null && inputTypeInfo is not null)
            _parity.ValidateInput(inputSchema, inputTypeInfo, _schemas.GetAll());
        if (outputSchema is not null && outputTypeInfo is not null)
            _parity.ValidateOutput(outputSchema, outputTypeInfo, _schemas.GetAll());

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
            InputSchema = _projector.ProjectInput(inputSchema, _schemas.GetAll()),
            OutputSchema = _projector.ProjectOutput(outputSchema, _schemas.GetAll()),
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
            outputSchemaHash,
            _preparedOutcomeProviders.Any(provider => provider.RequiresPreparedOutcome(tool.ToolName)));
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

    private static (JsonSerializerOptions Options, IReadOnlyList<JsonSerializerContext> Contexts) FreezeJsonOptions(AgentToolJsonOptions configured)
    {
        JsonSerializerOptions options;
        IJsonTypeInfoResolver[] sourceResolvers;
        try
        {
            var source = configured.SerializerOptions;
            sourceResolvers = source.TypeInfoResolverChain.ToArray();
            if (sourceResolvers.Any(resolver => resolver is not JsonSerializerContext))
            {
                throw new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                    "Agent Tool JSON resolver must be source-generated.");
            }
            // Do not clone a context-owned options object: the clone can carry
            // its read-only resolver state. Copy only the supported scalar and
            // converter settings into a fresh mutable shared template.
            options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = source.PropertyNamingPolicy,
                DictionaryKeyPolicy = source.DictionaryKeyPolicy,
                NumberHandling = source.NumberHandling,
                DefaultIgnoreCondition = source.DefaultIgnoreCondition,
                IgnoreReadOnlyProperties = source.IgnoreReadOnlyProperties,
                IgnoreReadOnlyFields = source.IgnoreReadOnlyFields,
                PropertyNameCaseInsensitive = source.PropertyNameCaseInsensitive,
                AllowTrailingCommas = source.AllowTrailingCommas,
                ReadCommentHandling = source.ReadCommentHandling,
                WriteIndented = source.WriteIndented,
                RespectNullableAnnotations = source.RespectNullableAnnotations,
                RespectRequiredConstructorParameters = source.RespectRequiredConstructorParameters
            };
            foreach (var converter in source.Converters)
                options.Converters.Add(converter);
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

        var contributors = configured.ContextContributors
            .Where(contributor => configured.EnabledModuleIds.Contains(contributor.ModuleId))
            .OrderBy(contributor => contributor.Order)
            .ThenBy(contributor => contributor.Id, StringComparer.Ordinal)
            .ToArray();
        var contributorIds = new HashSet<string>(StringComparer.Ordinal);
        var rootOwners = new Dictionary<Type, string>();
        var typeContracts = new Dictionary<Type, AgentToolJsonTypeContract>();
        var contributorContexts = sourceResolvers.OfType<JsonSerializerContext>().ToList();
        // Do not read TypeInfoResolver here. On .NET 10 its getter can
        // encapsulate a resolver-less options instance; generated context
        // constructors must be the first owner of this shared template.
        foreach (var contributor in contributors)
        {
            if (string.IsNullOrWhiteSpace(contributor.Id)
                || !contributorIds.Add(contributor.Id))
            {
                throw new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.DuplicateJsonContributor,
                    "Agent Tool JSON contributor IDs must be unique.");
            }

            foreach (var rootType in contributor.BindingRootTypes)
            {
                if (rootOwners.TryGetValue(rootType, out var owner))
                {
                    throw new AgentToolConfigurationException(
                        AgentToolStartupDiagnosticCodes.DuplicateJsonBindingRoot,
                        $"Agent Tool JSON binding root '{rootType}' is owned by both '{owner}' and '{contributor.Id}'.");
                }
                rootOwners[rootType] = contributor.Id;
            }

            foreach (var contract in contributor.TypeContracts)
            {
                if (contract.ClrType is null
                    || !string.Equals(contract.ContributorId, contributor.Id, StringComparison.Ordinal)
                    || contract.SchemaRef.Version is not > 0
                    || string.IsNullOrWhiteSpace(contract.ContractFingerprint.Value))
                {
                    throw new AgentToolConfigurationException(
                        AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                        $"JSON contributor '{contributor.Id}' declared an invalid CLR type contract.");
                }

                if (typeContracts.TryGetValue(contract.ClrType, out var existingContract))
                {
                    if (existingContract.IsBindingRoot || contract.IsBindingRoot)
                    {
                        throw new AgentToolConfigurationException(
                            AgentToolStartupDiagnosticCodes.DuplicateJsonBindingRoot,
                            $"JSON binding root '{contract.ClrType}' has more than one owner.");
                    }
                    if (!Equals(existingContract.ContractFingerprint, contract.ContractFingerprint)
                        || !Equals(existingContract.SchemaRef, contract.SchemaRef))
                    {
                        throw new AgentToolConfigurationException(
                            AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                            $"Nested JSON contract '{contract.ClrType}' is not equivalent across contributors.");
                    }
                }
                else
                {
                    typeContracts.Add(contract.ClrType, contract);
                }
            }

            JsonSerializerContext context;
            try
            {
                // System.Text.Json source-generated contexts take ownership of
                // their options and mark them read-only. Give each generated
                // contributor an equivalent frozen template; the runtime
                // contract is the normalized options fingerprint, not a
                // mutable shared resolver chain (which STJ cannot support for
                // multiple generated contexts).
                var contextOptions = new JsonSerializerOptions(options);
                context = contributor.Create(contextOptions)
                    ?? throw new InvalidOperationException("JSON contributor returned null context.");
                if (!string.Equals(JsonOptionsFingerprint(context.Options), JsonOptionsFingerprint(options), StringComparison.Ordinal))
                    throw new AgentToolConfigurationException(
                        AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                        $"JSON contributor '{contributor.Id}' did not preserve the shared options contract.");
            }
            catch (Exception exception) when (exception is not AgentToolConfigurationException)
            {
                throw new AgentToolConfigurationException(
                    AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration,
                    $"Agent Tool JSON contributor '{contributor.Id}' could not create its context.",
                    exception);
            }

            contributorContexts.Add(context);
        }

        options.TypeInfoResolver = EmptyJsonTypeInfoResolver.Instance;
        options.MakeReadOnly();
        return (options, contributorContexts);
    }

    private static string JsonOptionsFingerprint(JsonSerializerOptions options)
        => string.Join("|",
            options.PropertyNamingPolicy?.GetType().AssemblyQualifiedName ?? string.Empty,
            options.DictionaryKeyPolicy?.GetType().AssemblyQualifiedName ?? string.Empty,
            options.NumberHandling,
            options.DefaultIgnoreCondition,
            options.IgnoreReadOnlyProperties,
            options.IgnoreReadOnlyFields,
            options.PropertyNameCaseInsensitive,
            options.AllowTrailingCommas,
            options.ReadCommentHandling,
            options.WriteIndented,
            options.Converters.Count);

    private static JsonTypeInfo? ResolveTypeInfo(JsonSerializerOptions options, Type? type, IReadOnlyList<JsonSerializerContext> contexts)
    {
        if (type is null)
            return null;

        try
        {
            try
            {
                var resolved = options.GetTypeInfo(type);
                if (resolved is not null)
                    return resolved;
            }
            catch (NotSupportedException) { }
            catch (InvalidOperationException) { }
            foreach (var context in contexts)
            {
                var info = context.GetTypeInfo(type);
                if (info is not null)
                    return info;
            }
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.MissingJsonTypeInfo,
                "Application-owned source-generated JsonTypeInfo is missing.");
        }
        catch (NotSupportedException exception)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.MissingJsonTypeInfo,
                "Application-owned source-generated JsonTypeInfo is missing.",
                exception);
        }
    }

    private sealed class EmptyJsonTypeInfoResolver : IJsonTypeInfoResolver
    {
        public static EmptyJsonTypeInfoResolver Instance { get; } = new();
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => null;
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
