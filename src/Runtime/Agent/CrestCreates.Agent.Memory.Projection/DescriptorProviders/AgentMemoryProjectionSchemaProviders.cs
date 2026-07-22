using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using System.Runtime.CompilerServices;

namespace CrestCreates.Agent.Memory.Projection.DescriptorProviders;

/// <summary>
/// Sole owner of all shared read schemas (canonical-hash, source-grant, diagnostic,
/// block, item, build-pack-input/output, expand-source-input/output).
/// Write-only schemas remain in Agent.Memory.Tools.
/// MCP-specific schemas (ctx-recall) in Mcp.Memory.
/// </summary>
public static class AgentMemoryProjectionSchemaProviders
{
    private static int _registered;

    [ModuleInitializer]
    internal static void Register()
        => EnsureRegistered();

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            DescriptorProviderRegistry.Register<SchemaDescriptor>(new Schemas());
    }

    private sealed class Schemas : IDescriptorProvider<SchemaDescriptor>
    {
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => ProjectionSchemaDefinitions.AllSchemas;
    }
}

internal static class ProjectionSchemaDefinitions
{
    private static VersionedDescriptorRef<SchemaDescriptor> Ref(string id) => new(id, 1);

    public static readonly IReadOnlyList<SchemaDescriptor> AllSchemas =
    [
        Schema("canonical-hash",
            ("Value", "string", true, false),
            ("AlgorithmVersion", "string", true, false),
            ("ContractVersion", "string", true, false),
            ("CanonicalShapeVersion", "string", true, false)),
        Schema("source-grant",
            ("GrantId", "string", true, false),
            ("SourceKind", "string", true, false),
            ("ExpiresAt", "datetime", true, false)),
        Schema("diagnostic",
            ("Code", "string", true, false),
            ("Severity", "string", true, false)),
        Schema("block",
            ("Content", "string", true, false),
            ("CanonicalContentHash", "object", true, false),
            ("SourceGrants", "object", false, false)),
        Schema("item",
            ("MemoryHandle", "string", true, false),
            ("Kind", "string", true, false),
            ("Content", "string", true, false),
            ("CanonicalContentHash", "object", true, false),
            ("Confidence", "string", true, false),
            ("MemoryStatus", "string", true, false),
            ("IsAuthoritative", "bool", false, false),
            ("Tags", "string", false, false),
            ("SourceGrants", "object", false, false)),
        Schema("build-pack-input",
            ("MemoryHandles", "string", false, false),
            ("Kinds", "string", false, false),
            ("Tags", "string", false, false),
            ("MaximumCount", "int", true, false),
            ("CharacterBudget", "int", true, false),
            ("MinimumConfidence", "string", false, false)),
        Schema("build-pack-output",
            ("OperationStatus", "string", true, false),
            ("Items", "object", false, false),
            ("ReturnedCount", "int", false, false),
            ("WasTruncated", "bool", false, false),
            ("IsAuthoritative", "bool", false, false),
            ("Diagnostics", "object", false, false)),
        Schema("expand-source-input",
            ("GrantId", "string", true, false),
            ("MaximumCharacters", "int", true, false)),
        Schema("expand-source-output",
            ("OperationStatus", "string", true, false),
            ("SanitizedContent", "string", false, true),
            ("CanonicalContentHash", "object", false, true),
            ("WasTruncated", "bool", false, false),
            ("Diagnostics", "object", false, false))
    ];

    private static SchemaDescriptor Schema(string id, params (string Name, string Type, bool Required, bool Nullable)[] fields)
        => new()
        {
            Id = id, Name = id, Version = 1, State = DescriptorState.Active,
            Fields = fields.Select(field => new SchemaFieldDescriptor
            {
                Name = field.Name, FieldType = field.Type, IsRequired = field.Required, IsNullable = field.Nullable,
                ObjectSchema = field.Type == "object" ? Ref(NestedSchemaId(id, field.Name)) : null,
                IsCollection = field.Name is "MemoryHandles" or "Kinds" or "Tags" or "Items" or "Diagnostics" or "Blocks" or "SourceGrants",
                CollectionElementType = field.Name is "MemoryHandles" or "Kinds" or "Tags" ? "string" : null
            }).ToArray()
        };

    private static string NestedSchemaId(string schemaId, string fieldName)
        => fieldName switch
        {
            "CanonicalContentHash" => "canonical-hash",
            "SourceGrants" => "source-grant",
            "Diagnostics" => "diagnostic",
            "Items" => "item",
            "Blocks" => "block",
            _ => throw new InvalidOperationException($"Unknown nested schema field '{schemaId}.{fieldName}'.")
        };
}
