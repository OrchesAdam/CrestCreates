using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Schema.Abstractions;
using System.Runtime.CompilerServices;

namespace CrestCreates.Agent.Memory.Tools;

internal static class AgentMemoryToolDescriptorProviders
{
    [ModuleInitializer]
    internal static void Register()
    {
        DescriptorProviderRegistry.Register<SchemaDescriptor>(new Schemas());
        DescriptorProviderRegistry.Register<CapabilityDescriptor>(new Capabilities());
    }

    private sealed class Schemas : IDescriptorProvider<SchemaDescriptor>
    {
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => Definitions.AllSchemas;
    }

    private sealed class Capabilities : IDescriptorProvider<CapabilityDescriptor>
    {
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => Definitions.AllCapabilities;
    }
}

internal static class Definitions
{
    private static VersionedDescriptorRef<SchemaDescriptor> Ref(string id) => new(id, 1);

    // Shared read schemas (canonical-hash, source-grant, diagnostic, block, item,
    // build-pack-input/output, expand-source-input/output) are owned by
    // Agent.Memory.Projection. Only write-specific schemas stay here.
    public static readonly IReadOnlyList<SchemaDescriptor> AllSchemas =
    [
        Schema("candidate", ("CandidateHandle", "string", true, false), ("Kind", "string", true, false), ("Content", "string", true, false), ("CanonicalContentHash", "object", true, false), ("Confidence", "string", true, false), ("CandidateStatus", "string", true, false), ("IsAuthoritative", "bool", false, false), ("SourceGrants", "object", false, false)),
        Schema("compress-history-input", ("HistorySourceHandle", "string", true, false)),
        Schema("extract-candidates-input", ("ContextHandle", "string", true, false)),
        Schema("promote-candidate-input", ("CandidateHandle", "string", true, false), ("Explanation", "string", false, true)),
        Schema("reject-candidate-input", ("CandidateHandle", "string", true, false), ("Explanation", "string", false, true)),
        Schema("supersede-item-input", ("MemoryHandle", "string", true, false), ("ReplacementCandidateHandle", "string", true, false), ("Explanation", "string", false, true)),
        Schema("compress-history-output", ("OperationStatus", "string", true, false), ("ContextHandle", "string", false, true), ("SourceKind", "string", false, true), ("Blocks", "object", false, false), ("BlockCount", "int", false, false), ("Diagnostics", "object", false, false)),
        Schema("extract-candidates-output", ("OperationStatus", "string", true, false), ("ContextHandle", "string", false, true), ("Candidates", "object", false, false), ("CandidateCount", "int", false, false), ("Diagnostics", "object", false, false)),
        Schema("promote-candidate-output", ("OperationStatus", "string", true, false), ("Item", "object", false, true), ("Diagnostics", "object", false, false)),
        Schema("reject-candidate-output", ("OperationStatus", "string", true, false), ("CandidateHandle", "string", false, true), ("CandidateStatus", "string", false, true), ("IsAuthoritative", "bool", false, false), ("Diagnostics", "object", false, false)),
        Schema("supersede-item-output", ("OperationStatus", "string", true, false), ("Item", "object", false, true), ("SupersededMemoryHandle", "string", false, true), ("ActiveMemoryHandle", "string", false, true), ("Diagnostics", "object", false, false))
    ];

    public static readonly IReadOnlyList<CapabilityDescriptor> AllCapabilities =
    [
        Capability(AgentMemoryToolCapabilityIds.BuildPack, CapabilityKind.Query, "Crest.AgentMemory.Recall", "build-pack-input", "build-pack-output", CapabilityRiskLevel.Low),
        Capability(AgentMemoryToolCapabilityIds.ExpandSource, CapabilityKind.Query, "Crest.AgentMemory.Expand", "expand-source-input", "expand-source-output", CapabilityRiskLevel.Medium),
        Capability(AgentMemoryToolCapabilityIds.CompressHistory, CapabilityKind.Command, "Crest.AgentMemory.Compress", "compress-history-input", "compress-history-output", CapabilityRiskLevel.Medium),
        Capability(AgentMemoryToolCapabilityIds.ExtractCandidates, CapabilityKind.Command, "Crest.AgentMemory.Extract", "extract-candidates-input", "extract-candidates-output", CapabilityRiskLevel.Medium),
        Capability(AgentMemoryToolCapabilityIds.PromoteCandidate, CapabilityKind.Command, "Crest.AgentMemory.Promote", "promote-candidate-input", "promote-candidate-output", CapabilityRiskLevel.Medium),
        Capability(AgentMemoryToolCapabilityIds.RejectCandidate, CapabilityKind.Command, "Crest.AgentMemory.Reject", "reject-candidate-input", "reject-candidate-output", CapabilityRiskLevel.Medium),
        Capability(AgentMemoryToolCapabilityIds.SupersedeItem, CapabilityKind.Command, "Crest.AgentMemory.Supersede", "supersede-item-input", "supersede-item-output", CapabilityRiskLevel.High)
    ];

    private static SchemaDescriptor Schema(string id, params (string Name, string Type, bool Required, bool Nullable)[] fields)
        => new()
        {
            Id = id, Name = id, Version = 1, State = DescriptorState.Active,
            Fields = fields.Select(field => new SchemaFieldDescriptor
            {
                Name = field.Name, FieldType = field.Type, IsRequired = field.Required, IsNullable = field.Nullable,
                ObjectSchema = field.Type == "object" ? Ref(NestedSchemaId(id, field.Name)) : null,
                IsCollection = field.Name is "MemoryHandles" or "Kinds" or "Tags" or "Items" or "Diagnostics" or "Blocks" or "Candidates" or "SourceGrants",
                CollectionElementType = field.Name is "MemoryHandles" or "Kinds" or "Tags" ? "string" : null
            }).ToArray()
        };

    private static string NestedSchemaId(string schemaId, string fieldName)
        => fieldName switch
        {
            "CanonicalContentHash" => "canonical-hash",
            "SourceGrants" => "source-grant",
            "Diagnostics" => "diagnostic",
            "Items" or "Item" => "item",
            "Blocks" => "block",
            "Candidates" => "candidate",
            _ => throw new InvalidOperationException($"Unknown nested schema field '{schemaId}.{fieldName}'.")
        };

    private static CapabilityDescriptor Capability(string id, CapabilityKind kind, string permission, string input, string output, CapabilityRiskLevel risk)
        => new()
        {
            Id = id, Name = id, Version = 1, State = DescriptorState.Active, CapabilityKind = kind,
            InputSchema = Ref(input), OutputSchema = Ref(output), Permissions = [permission], RiskLevel = risk, ProjectionKind = CapabilityProjectionKind.Native
        };
}
