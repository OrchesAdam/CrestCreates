using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.AgentDraftContractGenerator;

internal static class AgentDraftContractDiagnostics
{
    private const string Category = "AgentDraftContract";

    public static readonly DiagnosticDescriptor NoSpecForDescriptor = new(
        id: "ADP001",
        title: "No contract spec for descriptor type",
        messageFormat: "No contract spec exists for public persistent descriptor type '{0}'. Add a class decorated with [AgentDraftContractSpec(Kind = DescriptorKind.{0})].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoClassification = new(
        id: "ADP002",
        title: "Property has no classification",
        messageFormat: "Public persistent property '{0}' on '{1}' has no primary classification. Add [AgentDraftField], [AgentDraftReference], [AgentDraftPreserve], or [AgentDraftUnsupported].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleClassifications = new(
        id: "ADP003",
        title: "Property has multiple classifications",
        messageFormat: "Public persistent property '{0}' on '{1}' has more than one primary classification. Exactly one is required.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingReason = new(
        id: "ADP004",
        title: "Missing reason for Preserve/Unsupported",
        messageFormat: "Property '{0}' on '{1}' is classified as {2} but is missing a non-empty Reason.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingCreateStrategy = new(
        id: "ADP005",
        title: "Missing create strategy for Preserve",
        messageFormat: "Preserve property '{0}' on '{1}' is missing a valid create strategy.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidRequiredOnCreate = new(
        id: "ADP006",
        title: "Invalid RequiredOnCreate usage",
        messageFormat: "RequiredOnCreate is applied to property '{0}' on '{1}' with an invalid classification or shape. RequiredOnCreate is valid only on EditableScalar or EditableReference fields.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NullableConflict = new(
        id: "ADP007",
        title: "Nullable/Collection conflict",
        messageFormat: "Property '{0}' on '{1}' has a Nullable or Collection modifier that conflicts with the CLR shape or classification.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidContractName = new(
        id: "ADP008",
        title: "Invalid or duplicate ContractName",
        messageFormat: "ContractName '{0}' on property '{1}' is invalid or duplicated within the contract for '{2}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnstableContract = new(
        id: "ADP009",
        title: "Unstable contract",
        messageFormat: "The generated contract for '{0}' would be unstable, ambiguous, or non-deterministic.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedReference = new(
        id: "ADP010",
        title: "Unsupported reference shape",
        messageFormat: "Reference shape '{0}' on property '{1}' of '{2}' is unsupported or incompatible at compile time.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args)
    {
        return Diagnostic.Create(descriptor, location, args);
    }
}
