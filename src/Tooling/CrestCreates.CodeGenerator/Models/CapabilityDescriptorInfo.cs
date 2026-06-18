using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.Models;

public sealed class CapabilityDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string CapabilityKind { get; set; } = "Command";
    public string InputSchemaId { get; set; } = string.Empty;
    public int InputSchemaVersion { get; set; } = 1;
    public string OutputSchemaId { get; set; } = string.Empty;
    public int OutputSchemaVersion { get; set; } = 1;
    public string Permission { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "Medium";
    public List<string> SemanticTags { get; set; } = new();
    public List<string> Aliases { get; set; } = new();
}
