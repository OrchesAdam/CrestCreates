using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools.Tests;

internal static class AgentToolRuntimeTestFixture
{
    public static AgentCapabilityToolDescriptor Tool(
        string id,
        string capabilityId,
        string toolName,
        DescriptorState state = DescriptorState.Active,
        string? expectedCapabilityHash = null,
        AgentToolSideEffectKind sideEffect = AgentToolSideEffectKind.Unknown,
        AgentToolAuditMode audit = AgentToolAuditMode.BestEffort)
        => new()
        {
            Id = id,
            Name = id,
            Version = 1,
            State = state,
            Capability = new CapabilityProjectionReference(
                capabilityId,
                1,
                VersionSelectionMode.Exact,
                expectedCapabilityHash),
            ToolName = toolName,
            Title = "Test tool",
            Description = "A test Agent Tool.",
            SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
            SideEffectKind = sideEffect,
            ApprovalMode = AgentToolApprovalMode.None,
            Budget = new AgentToolBudgetRequirement { Category = "tests", CostUnits = 1 },
            AuditMode = audit,
            AllowedAgentRoles = new[] { "operator" }
        };

    public static CapabilityDescriptor Capability(
        string id,
        int version = 1,
        VersionedDescriptorRef<SchemaDescriptor>? input = null,
        VersionedDescriptorRef<SchemaDescriptor>? output = null)
        => new()
        {
            Id = id,
            Name = id,
            Version = version,
            State = DescriptorState.Active,
            CapabilityKind = CapabilityKind.Query,
            RiskLevel = CapabilityRiskLevel.Low,
            InputSchema = input,
            OutputSchema = output
        };

    public static SchemaDescriptor Schema(string id)
        => new()
        {
            Id = id,
            Name = id,
            Version = 1,
            State = DescriptorState.Active,
            Fields = new[]
            {
                new SchemaFieldDescriptor
                {
                    Name = nameof(TestDto.Value),
                    FieldType = "int",
                    IsRequired = false,
                    IsNullable = false
                }
            }
        };

    public static AgentToolRegistry BuildToolRegistry(
        params AgentCapabilityToolDescriptor[] descriptors)
    {
        var registry = new AgentToolRegistry(
            new RegistryValidationEngine<AgentCapabilityToolDescriptor>(
                new[] { new AgentToolDescriptorValidator() }));
        registry.Build(new[] { new TestDescriptorProvider<AgentCapabilityToolDescriptor>(descriptors) });
        return registry;
    }

    public static CapabilityRegistry BuildCapabilityRegistry(params CapabilityDescriptor[] descriptors)
    {
        var registry = new CapabilityRegistry(
            new RegistryValidationEngine<CapabilityDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityDescriptor>>()));
        registry.Build(new[] { new TestDescriptorProvider<CapabilityDescriptor>(descriptors) });
        return registry;
    }

    public static SchemaRegistry BuildSchemaRegistry(params SchemaDescriptor[] descriptors)
    {
        var registry = new SchemaRegistry(
            new RegistryValidationEngine<SchemaDescriptor>(
                Array.Empty<IRegistryValidator<SchemaDescriptor>>()));
        registry.Build(new[] { new TestDescriptorProvider<SchemaDescriptor>(descriptors) });
        return registry;
    }

    public static AgentToolRuntimeSnapshotBuilder SnapshotBuilder(
        AgentToolRegistry tools,
        CapabilityRegistry capabilities,
        SchemaRegistry schemas,
        AgentToolJsonOptions? json = null)
    {
        json ??= GeneratedJsonOptions();
        return new AgentToolRuntimeSnapshotBuilder(
            tools,
            capabilities,
            schemas,
            new AgentToolCapabilityResolver(capabilities),
            new AgentToolSchemaResolver(schemas),
            new AgentToolJsonSchemaProjector(new SchemaJsonContractProjector()),
            new AgentToolSchemaParityValidator(new SchemaJsonTypeInfoParityValidator()),
            new AgentToolEffectiveGovernanceDeriver(),
            new TestCanonicalHashComputer(),
            json);
    }

    public static AgentToolJsonOptions GeneratedJsonOptions()
    {
        var options = new AgentToolJsonOptions();
        options.SerializerOptions.TypeInfoResolverChain.Add(AgentToolTestJsonContext.Default);
        return options;
    }

    public static string Hash(IDescriptor descriptor)
        => $"hash:{descriptor.Namespace}:{descriptor.Id}:{(descriptor as IVersionedDescriptor)?.Version ?? 0}";

    public static void RegisterNoPayloadBinding(AgentCapabilityToolDescriptor tool)
        => AgentToolBindingRegistry.Register(new AgentToolBindingContract
        {
            ToolDescriptorId = tool.Id,
            ToolDescriptorVersion = tool.Version,
            BindInputAsync = static (_, _, _) => ValueTask.FromResult<object?>(null),
            SerializeOutputAsync = static (_, _, _) => ValueTask.FromResult<JsonElement?>(null)
        });

    public static void RegisterDtoBinding(AgentCapabilityToolDescriptor tool)
    {
        AgentToolJsonContractRegistry.RegisterInputType(typeof(TestDto));
        AgentToolJsonContractRegistry.RegisterOutputType(typeof(TestDto));
        AgentToolBindingRegistry.Register(new AgentToolBindingContract
        {
            ToolDescriptorId = tool.Id,
            ToolDescriptorVersion = tool.Version,
            InputType = typeof(TestDto),
            OutputType = typeof(TestDto),
            BindInputAsync = static (_, _, _) => ValueTask.FromResult<object?>(new TestDto()),
            SerializeOutputAsync = static (_, _, _) => ValueTask.FromResult<JsonElement?>(null)
        });
    }

    public static void RegisterInputDtoBinding(AgentCapabilityToolDescriptor tool)
    {
        AgentToolJsonContractRegistry.RegisterInputType(typeof(TestDto));
        AgentToolBindingRegistry.Register(new AgentToolBindingContract
        {
            ToolDescriptorId = tool.Id,
            ToolDescriptorVersion = tool.Version,
            InputType = typeof(TestDto),
            BindInputAsync = static (_, _, _) => ValueTask.FromResult<object?>(new TestDto()),
            SerializeOutputAsync = static (_, _, _) => ValueTask.FromResult<JsonElement?>(null)
        });
    }
}

internal sealed class TestDescriptorProvider<TDescriptor> : IDescriptorProvider<TDescriptor>
    where TDescriptor : class, IDescriptor
{
    private readonly IReadOnlyList<TDescriptor> _descriptors;

    public TestDescriptorProvider(params TDescriptor[] descriptors)
        => _descriptors = descriptors;

    public IReadOnlyList<TDescriptor> GetDescriptors() => _descriptors;
}

internal sealed class TestCanonicalHashComputer : ICanonicalHashComputer
{
    public CanonicalHash ComputeContractHash(IDescriptor descriptor, CanonicalHashScope scope)
        => Create(AgentToolRuntimeTestFixture.Hash(descriptor), "Contract");

    public CanonicalHash ComputeDefinitionHash(IDescriptor descriptor, CanonicalHashScope scope)
        => Create(AgentToolRuntimeTestFixture.Hash(descriptor) + ":definition", "Definition");

    public CanonicalHash ComputeFromProjection(CanonicalHashProjectionResult projection)
        => Create("projection", projection.Metadata.Purpose);

    private static CanonicalHash Create(string value, string purpose)
        => new()
        {
            Value = value,
            Algorithm = "test",
            AlgorithmVersion = "test-v1",
            ArtifactKind = "Descriptor",
            Scope = "InternalFull",
            Purpose = purpose,
            ContractVersion = "test-v1",
            CanonicalShapeVersion = "test-v1"
        };
}

internal sealed class TestDto
{
    public int Value { get; set; }
}

[JsonSerializable(typeof(TestDto))]
internal partial class AgentToolTestJsonContext : JsonSerializerContext
{
}
