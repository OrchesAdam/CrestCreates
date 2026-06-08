using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.Models;

internal sealed class FormDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SchemaId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public List<FormFieldInfo> Fields { get; set; } = new();
}

internal sealed class FormFieldInfo
{
    public string SchemaFieldName { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int Order { get; set; }
}
