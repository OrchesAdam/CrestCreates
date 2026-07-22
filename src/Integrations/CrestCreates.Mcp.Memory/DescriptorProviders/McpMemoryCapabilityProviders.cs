using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Schema.Abstractions;
using System.Runtime.CompilerServices;

namespace CrestCreates.Mcp.Memory.DescriptorProviders;

internal static class McpMemoryCapabilityProviders
{
    [ModuleInitializer]
    internal static void Register()
    {
        DescriptorProviderRegistry.Register<SchemaDescriptor>(new Schemas());
        DescriptorProviderRegistry.Register<CapabilityDescriptor>(new Capabilities());
    }

    private sealed class Schemas : IDescriptorProvider<SchemaDescriptor>
    {
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => McpMemorySchemas.All;
    }

    private sealed class Capabilities : IDescriptorProvider<CapabilityDescriptor>
    {
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => McpMemoryCapabilities.All;
    }
}

internal static class McpMemorySchemas
{
    private static VersionedDescriptorRef<SchemaDescriptor> Ref(string id) => new(id, 1);

    public static readonly IReadOnlyList<SchemaDescriptor> All =
    [
        new SchemaDescriptor
        {
            Id = "ctx-recall-input", Name = "RecallAgentContextInput", Version = 1, State = DescriptorState.Active,
            Fields =
            [
                new SchemaFieldDescriptor { Name = "ContextHandle", FieldType = "string", IsRequired = true },
                new SchemaFieldDescriptor { Name = "MaximumCharacters", FieldType = "int", IsRequired = true }
            ]
        },
        new SchemaDescriptor
        {
            Id = "ctx-recall-output", Name = "RecallAgentContextResult", Version = 1, State = DescriptorState.Active,
            Fields =
            [
                new SchemaFieldDescriptor { Name = "OperationStatus", FieldType = "string", IsRequired = true, IsNullable = false },
                new SchemaFieldDescriptor { Name = "SanitizedContent", FieldType = "string", IsRequired = false, IsNullable = true },
                new SchemaFieldDescriptor { Name = "CanonicalContentHash", FieldType = "object", IsRequired = false, IsNullable = true, ObjectSchema = Ref("canonical-hash") },
                new SchemaFieldDescriptor { Name = "WasTruncated", FieldType = "bool", IsRequired = false, IsNullable = false },
                new SchemaFieldDescriptor { Name = "Blocks", FieldType = "object", IsRequired = false, IsNullable = false, ObjectSchema = Ref("block"), IsCollection = true },
                new SchemaFieldDescriptor { Name = "BlockCount", FieldType = "int", IsRequired = false, IsNullable = false },
                new SchemaFieldDescriptor { Name = "Diagnostics", FieldType = "object", IsRequired = false, IsNullable = false, ObjectSchema = Ref("diagnostic"), IsCollection = true }
            ]
        }
    ];
}

internal static class McpMemoryCapabilities
{
    private static VersionedDescriptorRef<SchemaDescriptor> Ref(string id) => new(id, 1);

    public static readonly IReadOnlyList<CapabilityDescriptor> All =
    [
        // ctx_recall
        new CapabilityDescriptor
        {
            Id = "mcp.ctx_recall", Name = "ctx_recall", Version = 1, State = DescriptorState.Active,
            CapabilityKind = CapabilityKind.Query,
            InputSchema = Ref("ctx-recall-input"),
            OutputSchema = Ref("ctx-recall-output"),
            Permissions = ["Mcp.CtxRecall"],
            RiskLevel = CapabilityRiskLevel.Low,
            ProjectionKind = CapabilityProjectionKind.Native
        },
        // ctx_expand
        new CapabilityDescriptor
        {
            Id = "mcp.ctx_expand", Name = "ctx_expand", Version = 1, State = DescriptorState.Active,
            CapabilityKind = CapabilityKind.Query,
            InputSchema = Ref("expand-source-input"),
            OutputSchema = Ref("expand-source-output"),
            Permissions = ["Mcp.CtxExpand"],
            RiskLevel = CapabilityRiskLevel.Medium,
            ProjectionKind = CapabilityProjectionKind.Native
        },
        // memory_recall
        new CapabilityDescriptor
        {
            Id = "mcp.memory_recall", Name = "memory_recall", Version = 1, State = DescriptorState.Active,
            CapabilityKind = CapabilityKind.Query,
            InputSchema = Ref("build-pack-input"),
            OutputSchema = Ref("build-pack-output"),
            Permissions = ["Mcp.MemoryRecall"],
            RiskLevel = CapabilityRiskLevel.Low,
            ProjectionKind = CapabilityProjectionKind.Native
        },
        // memory_source_expand
        new CapabilityDescriptor
        {
            Id = "mcp.memory_source_expand", Name = "memory_source_expand", Version = 1, State = DescriptorState.Active,
            CapabilityKind = CapabilityKind.Query,
            InputSchema = Ref("expand-source-input"),
            OutputSchema = Ref("expand-source-output"),
            Permissions = ["Mcp.MemorySourceExpand"],
            RiskLevel = CapabilityRiskLevel.Medium,
            ProjectionKind = CapabilityProjectionKind.Native
        }
    ];
}
