using System.Text.RegularExpressions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Mcp;

namespace CrestCreates.Mcp;

public sealed partial class McpToolDescriptorValidator : IRegistryValidator<McpToolDescriptor>
{
    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<McpToolDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var descriptor in descriptors)
        {
            ValidateShape(descriptor, issues);
            ValidateCapabilityReference(descriptor, issues);
        }

        foreach (var duplicate in descriptors
                     .GroupBy(
                         descriptor => (descriptor.Id, descriptor.Version),
                         EqualityComparer<(string Id, int Version)>.Default)
                     .Where(group => group.Count() > 1))
        {
            AddError(
                issues,
                "MCP101",
                $"MCP Tool descriptor identity '{duplicate.Key.Id}' v{duplicate.Key.Version} is not unique.");
        }

        foreach (var duplicate in descriptors
                     .Where(descriptor => descriptor.State == DescriptorState.Active)
                     .GroupBy(descriptor => descriptor.ToolName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            AddError(issues, "MCP102", $"Active MCP ToolName '{duplicate.Key}' is not unique.");
        }

        return new ValidationReport(issues);
    }

    private static void ValidateShape(McpToolDescriptor descriptor, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id)
            || descriptor.Id.Any(char.IsWhiteSpace)
            || string.IsNullOrWhiteSpace(descriptor.Name)
            || descriptor.Version <= 0
            || string.IsNullOrWhiteSpace(descriptor.Description)
            || string.IsNullOrWhiteSpace(descriptor.ToolName)
            || !ToolNamePattern().IsMatch(descriptor.ToolName)
            || descriptor.AnnotationOverrides is null)
        {
            AddError(issues, "MCP116", $"MCP Tool descriptor '{descriptor.Id}' has an invalid contract.");
        }
    }

    private static void ValidateCapabilityReference(
        McpToolDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        var capability = descriptor.Capability;
        var validSelection = capability.SelectionMode switch
        {
            VersionSelectionMode.Exact => capability.Version > 0,
            VersionSelectionMode.Latest => capability.Version == 0,
            _ => false
        };

        if (string.IsNullOrWhiteSpace(capability.Id) || !validSelection)
            AddError(issues, "MCP117", $"MCP Tool '{descriptor.Id}' has an unsupported Capability reference.");

        if (capability.ExpectedContractHash is not null)
            AddError(issues, "MCP119", $"MCP Tool '{descriptor.Id}' does not support ExpectedContractHash.");
    }

    private static void AddError(List<ValidationIssue> issues, string code, string message)
        => issues.Add(new ValidationIssue(SeverityLevel.Error, message)
        {
            Code = new DiagnosticCode(code)
        });

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolNamePattern();
}
