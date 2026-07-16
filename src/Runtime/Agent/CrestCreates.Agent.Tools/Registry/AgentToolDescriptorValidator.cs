using System.Text.RegularExpressions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

public sealed partial class AgentToolDescriptorValidator : IRegistryValidator<AgentCapabilityToolDescriptor>
{
    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<AgentCapabilityToolDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var issues = new List<ValidationIssue>();

        foreach (var descriptor in descriptors)
        {
            ValidateShape(descriptor, issues);
            ValidateCapabilityReference(descriptor, issues);
            ValidateLifecycle(descriptor, descriptors, issues);
        }

        foreach (var duplicate in descriptors
                     .GroupBy(descriptor => (descriptor.Id, descriptor.Version))
                     .Where(group => group.Count() > 1))
        {
            AddError(
                issues,
                AgentToolStartupDiagnosticCodes.DescriptorIdentityConflict,
                $"Agent Tool descriptor identity '{duplicate.Key.Id}' v{duplicate.Key.Version} is not unique.");
        }

        foreach (var duplicate in descriptors
                     .Where(descriptor => descriptor.State == DescriptorState.Active)
                     .GroupBy(descriptor => descriptor.ToolName, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            AddError(
                issues,
                AgentToolStartupDiagnosticCodes.ActiveToolNameConflict,
                $"Active Agent ToolName '{duplicate.Key}' is not unique.");
        }

        return new ValidationReport(issues);
    }

    private static void ValidateShape(
        AgentCapabilityToolDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        var budget = descriptor.Budget;
        var roles = descriptor.AllowedAgentRoles;
        var invalid = string.IsNullOrWhiteSpace(descriptor.Id)
            || descriptor.Id.Any(char.IsWhiteSpace)
            || string.IsNullOrWhiteSpace(descriptor.Name)
            || descriptor.Version <= 0
            || string.IsNullOrWhiteSpace(descriptor.ToolName)
            || !ToolNamePattern().IsMatch(descriptor.ToolName)
            || string.IsNullOrWhiteSpace(descriptor.Description)
            || budget is null
            || string.IsNullOrWhiteSpace(budget?.Category)
            || budget?.CostUnits <= 0
            || budget?.MaxCallsPerExecution is <= 0
            || roles is null
            || roles.Count == 0
            || roles.Any(role => string.IsNullOrWhiteSpace(role) || role == "*")
            || roles.Distinct(StringComparer.Ordinal).Count() != roles.Count
            || !IsKnownSelectionPolicy(descriptor.SelectionPolicy)
            || !IsKnownSideEffectKind(descriptor.SideEffectKind)
            || !IsKnownApprovalMode(descriptor.ApprovalMode)
            || !IsKnownAuditMode(descriptor.AuditMode)
            || descriptor.RiskFloor is { } floor && !IsKnownRisk(floor);

        if (invalid)
        {
            AddError(
                issues,
                AgentToolStartupDiagnosticCodes.InvalidDescriptorContract,
                $"Agent Tool descriptor '{descriptor.Id}' has an invalid contract.");
        }
    }

    private static void ValidateCapabilityReference(
        AgentCapabilityToolDescriptor descriptor,
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
        {
            AddError(
                issues,
                AgentToolStartupDiagnosticCodes.UnsupportedCapabilitySelection,
                $"Agent Tool '{descriptor.Id}' has an unsupported Capability reference.");
        }
    }

    private static void ValidateLifecycle(
        AgentCapabilityToolDescriptor descriptor,
        IReadOnlyList<AgentCapabilityToolDescriptor> descriptors,
        List<ValidationIssue> issues)
    {
        var knownState = IsKnownState(descriptor.State);
        var supersededBy = descriptor.SupersededById;
        var invalid = !knownState
            || supersededBy is not null && (string.IsNullOrWhiteSpace(supersededBy)
                || supersededBy.Any(char.IsWhiteSpace)
                || string.Equals(supersededBy, descriptor.Id, StringComparison.Ordinal))
            || (descriptor.State is DescriptorState.Active or DescriptorState.Draft
                && supersededBy is not null);

        if (!invalid && supersededBy is not null)
        {
            invalid = !descriptors.Any(candidate =>
                string.Equals(candidate.Id, supersededBy, StringComparison.Ordinal));
        }

        if (invalid)
        {
            AddError(
                issues,
                AgentToolStartupDiagnosticCodes.InvalidLifecycle,
                $"Agent Tool descriptor '{descriptor.Id}' has an invalid lifecycle relationship.");
        }
    }

    private static void AddError(List<ValidationIssue> issues, string code, string message)
        => issues.Add(new ValidationIssue(SeverityLevel.Error, message)
        {
            Code = new DiagnosticCode(code)
        });

    private static bool IsKnownSelectionPolicy(AgentToolSelectionPolicy value)
        => value is AgentToolSelectionPolicy.ExplicitOnly
            or AgentToolSelectionPolicy.AutomaticAllowed;

    private static bool IsKnownSideEffectKind(AgentToolSideEffectKind value)
        => value is AgentToolSideEffectKind.Unknown
            or AgentToolSideEffectKind.ReadOnly
            or AgentToolSideEffectKind.InternalWrite
            or AgentToolSideEffectKind.ExternalWrite
            or AgentToolSideEffectKind.Destructive;

    private static bool IsKnownApprovalMode(AgentToolApprovalMode value)
        => value is AgentToolApprovalMode.PolicyDriven
            or AgentToolApprovalMode.Required
            or AgentToolApprovalMode.None;

    private static bool IsKnownAuditMode(AgentToolAuditMode value)
        => value is AgentToolAuditMode.Required or AgentToolAuditMode.BestEffort;

    private static bool IsKnownRisk(CapabilityRiskLevel value)
        => value is CapabilityRiskLevel.Low
            or CapabilityRiskLevel.Medium
            or CapabilityRiskLevel.High
            or CapabilityRiskLevel.Critical;

    private static bool IsKnownState(DescriptorState value)
        => value is DescriptorState.Draft
            or DescriptorState.Active
            or DescriptorState.Deprecated
            or DescriptorState.Removed;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolNamePattern();
}
