using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.AgentToolGenerator;

internal static class AgentToolDiagnostics
{
    internal static readonly DiagnosticDescriptor InvalidCapabilityId = Create("ATP001", "CapabilityId is required");
    internal static readonly DiagnosticDescriptor InvalidToolName = Create("ATP002", "Agent ToolName is invalid");
    internal static readonly DiagnosticDescriptor DuplicateToolName = Create("ATP003", "Agent ToolName is duplicated in the container");
    internal static readonly DiagnosticDescriptor MissingRequiredText = Create("ATP004", "Agent Tool Description and BudgetCategory are required");
    internal static readonly DiagnosticDescriptor InvalidDescriptorVersion = Create("ATP005", "DescriptorVersion must be positive");
    internal static readonly DiagnosticDescriptor UnsupportedInputType = Create("ATP006", "Agent Tool input root is unsupported");
    internal static readonly DiagnosticDescriptor UnsupportedOutputType = Create("ATP007", "Agent Tool output root is unsupported");
    internal static readonly DiagnosticDescriptor InvalidDescriptorId = Create("ATP008", "Agent Tool DescriptorId is invalid or duplicated in the container");
    internal static readonly DiagnosticDescriptor InvalidEnum = Create("ATP009", "Agent Tool governance enum value is unsupported");
    internal static readonly DiagnosticDescriptor InvalidContainer = Create("ATP010", "Agent Tool container declaration is invalid");
    internal static readonly DiagnosticDescriptor InvalidSpec = Create("ATP011", "Agent Tool spec declaration is invalid");
    internal static readonly DiagnosticDescriptor NegativeCapabilityVersion = Create("ATP012", "CapabilityVersion cannot be negative");
    internal static readonly DiagnosticDescriptor InvalidAllowedRole = Create("ATP013", "AllowedAgentRoles must be non-empty, unique, and cannot contain wildcard roles");
    internal static readonly DiagnosticDescriptor InvalidBudget = Create("ATP014", "Agent Tool budget values are invalid");
    internal static readonly DiagnosticDescriptor ContradictorySideEffect = Create("ATP015", "Agent Tool side-effect classification contradicts the known Capability kind");
    internal static readonly DiagnosticDescriptor UnsafeGovernance = Create("ATP016", "Agent Tool approval or audit combination is unsafe");

    private static DiagnosticDescriptor Create(string id, string title)
        => new(id, title, title, "AgentToolProjection", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
