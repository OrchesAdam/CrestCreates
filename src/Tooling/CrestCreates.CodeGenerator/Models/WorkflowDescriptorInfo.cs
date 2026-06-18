using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.Models;

internal sealed class WorkflowDescriptorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? VariableSchemaId { get; set; }
    public int? VariableSchemaVersion { get; set; }
    public List<WorkflowStepInfo> Steps { get; set; } = new();
}

internal sealed class WorkflowStepInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? CapabilityId { get; set; }
    public string? HumanTaskId { get; set; }
    public string? SubWorkflowId { get; set; }
    public string OnError { get; set; } = "Fail";
}
