using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal static class AppServiceCompatibilityDiagnostics
{
    private const string Category = "CompatibilityProjection";

    /// <summary>
    /// CEP030: [CapabilityCompatibilityProjection] used on a type/method that is not a [CrestService].
    /// </summary>
    public static readonly DiagnosticDescriptor CEP030 = new(
        id: "CEP030",
        title: "Invalid CapabilityCompatibilityProjection target",
        messageFormat: "[CapabilityCompatibilityProjection] may only be used on a [CrestService] class or on a method declared by a [CrestService] class",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP031: [CapabilityCompatibilityProjection] and [DynamicApiIgnore] conflict on the same method.
    /// </summary>
    public static readonly DiagnosticDescriptor CEP031 = new(
        id: "CEP031",
        title: "Attribute conflict on compatibility projection",
        messageFormat: "Method '{0}' has both [CapabilityCompatibilityProjection] and [DynamicApiIgnore]; projection and suppression cannot coexist on the same method",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP032: Cannot derive HTTP method from method name convention.
    /// </summary>
    public static readonly DiagnosticDescriptor CEP032 = new(
        id: "CEP032",
        title: "Cannot derive HTTP method for compatibility projection",
        messageFormat: "Cannot derive HTTP method from method name '{0}'; method will be excluded from compatibility projection",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP033: Cannot derive permission from method name convention (warning).
    /// </summary>
    public static readonly DiagnosticDescriptor CEP033 = new(
        id: "CEP033",
        title: "Cannot derive permission for compatibility projection",
        messageFormat: "Cannot derive permission from method name '{0}'; endpoint will be generated without permission enforcement",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
