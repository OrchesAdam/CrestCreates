using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.Models;

public sealed class SchemaDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string ChangeKind { get; set; } = "Additive";
    public List<SchemaFieldInfo> Fields { get; set; } = new();
}

public sealed class SchemaFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool IsCollection { get; set; }
    public string? CollectionElementType { get; set; }
}
