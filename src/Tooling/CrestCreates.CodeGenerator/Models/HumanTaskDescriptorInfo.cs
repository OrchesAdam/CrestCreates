namespace CrestCreates.CodeGenerator.Models;

internal sealed class HumanTaskDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string FormId { get; set; } = string.Empty;
    public int FormVersion { get; set; }
    public string? InputSchemaId { get; set; }
    public int? InputSchemaVersion { get; set; }
    public string? OutputSchemaId { get; set; }
    public int? OutputSchemaVersion { get; set; }
    public string AssigneeStrategy { get; set; } = "SingleUser";
}
