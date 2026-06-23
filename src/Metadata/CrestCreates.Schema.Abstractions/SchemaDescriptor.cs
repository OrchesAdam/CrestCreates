using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Schema.Abstractions;

public sealed class SchemaDescriptor : IVersionedDescriptor
{
    public string Namespace => "schema";
    public DescriptorKind Kind => DescriptorKind.Schema;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    public int Version { get; init; }
    public SchemaChangeKind ChangeKind { get; init; }

    public IReadOnlyList<SchemaFieldDescriptor> Fields { get; init; } =
        Array.Empty<SchemaFieldDescriptor>();
    public IReadOnlyList<SchemaValidationRule> ValidationRules { get; init; } =
        Array.Empty<SchemaValidationRule>();
    public IReadOnlyList<VersionedDescriptorRef<SchemaDescriptor>> References { get; init; } =
        Array.Empty<VersionedDescriptorRef<SchemaDescriptor>>();
}

public sealed class SchemaFieldDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string FieldType { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsNullable { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
    public double? MaxValue { get; init; }
    public double? MinValue { get; init; }
    public string? Pattern { get; init; }
    public bool IsCollection { get; init; }
    public string? CollectionElementType { get; init; }
}

public sealed class SchemaValidationRule
{
    public string Name { get; init; } = string.Empty;
    public string Expression { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}
