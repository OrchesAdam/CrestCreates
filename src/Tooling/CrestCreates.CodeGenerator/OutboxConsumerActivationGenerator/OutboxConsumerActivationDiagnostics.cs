using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.OutboxConsumerActivationGenerator;

internal static class OutboxConsumerActivationDiagnosticCodes
{
    public const string NotPartialOrUnsupportedShapeValue = "CCOCA001";
    public const string NoRequiredConsumerInterfaceValue = "CCOCA002";
    public const string NoPublicConstructorOrMultiplePublicConstructorsValue = "CCOCA003";
    public const string UnsupportedParameterOrKeyedServiceProviderValue = "CCOCA004";
    public const string RequiredMemberNotSatisfiedValue = "CCOCA005";
    public const string ManualActivationConflictValue = "CCOCA006";
    public const string MissingActivationOrDependencyContractValue = "CCOCA007";
}

internal static class OutboxConsumerActivationDiagnostics
{
    private const string Category = "OutboxConsumerActivation";

    public static readonly DiagnosticDescriptor NotPartialOrUnsupportedShape = new(
        id: OutboxConsumerActivationDiagnosticCodes.NotPartialOrUnsupportedShapeValue,
        title: "[GenerateOutboxConsumerActivation] requires a non-generic, non-abstract, top-level partial class",
        messageFormat: "Type '{0}' marked with [GenerateOutboxConsumerActivation] must be a non-generic, non-abstract, top-level partial class",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoRequiredConsumerInterface = new(
        id: OutboxConsumerActivationDiagnosticCodes.NoRequiredConsumerInterfaceValue,
        title: "[GenerateOutboxConsumerActivation] type must implement IOutboxRequiredConsumer<T>",
        messageFormat: "Type '{0}' marked with [GenerateOutboxConsumerActivation] does not implement any closed IOutboxRequiredConsumer<T> interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoPublicConstructorOrMultiplePublicConstructors = new(
        id: OutboxConsumerActivationDiagnosticCodes.NoPublicConstructorOrMultiplePublicConstructorsValue,
        title: "Type must have exactly one public constructor",
        messageFormat: "Type '{0}' must have exactly one public constructor but has {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedParameterOrKeyedServiceProvider = new(
        id: OutboxConsumerActivationDiagnosticCodes.UnsupportedParameterOrKeyedServiceProviderValue,
        title: "Constructor parameter is not a supported DI dependency",
        messageFormat: "Constructor parameter '{0}' on type '{1}' is not supported: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequiredMemberNotSatisfied = new(
        id: OutboxConsumerActivationDiagnosticCodes.RequiredMemberNotSatisfiedValue,
        title: "Required member cannot be satisfied by the selected constructor",
        messageFormat: "Type '{0}' has required member '{1}' that cannot be satisfied by the selected constructor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ManualActivationConflict = new(
        id: OutboxConsumerActivationDiagnosticCodes.ManualActivationConflictValue,
        title: "Type already has a manual IOutboxConsumerActivation implementation",
        messageFormat: "Type '{0}' already has a manual IOutboxConsumerActivation implementation or conflicting member; remove the duplicate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingActivationOrDependencyContract = new(
        id: OutboxConsumerActivationDiagnosticCodes.MissingActivationOrDependencyContractValue,
        title: "Missing activation or DI dependency contract",
        messageFormat: "Type '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
