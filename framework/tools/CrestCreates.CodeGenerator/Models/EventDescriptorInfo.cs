namespace CrestCreates.CodeGenerator.Models;

internal sealed class EventDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string PayloadSchemaId { get; set; } = string.Empty;
    public int PayloadSchemaVersion { get; set; }
    public string Category { get; set; } = "Domain";
    public string Semantic { get; set; } = "Fact";
    public string Importance { get; set; } = "Business";
    public string ChangeKind { get; set; } = "Additive";
}
