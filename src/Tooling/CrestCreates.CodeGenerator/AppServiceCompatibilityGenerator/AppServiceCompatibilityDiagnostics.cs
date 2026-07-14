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
    /// CEP034: Projected methods overload the same action name.
    /// Compatibility projection does not support method overloads because
    /// CapabilityId, EndpointId, binding method, and invoker class all use
    /// only the method name.
    /// </summary>
    public static readonly DiagnosticDescriptor CEP034 = new(
        id: "CEP034",
        title: "Projected AppService method overloads are not supported",
        messageFormat: "Method '{0}' on '{1}' overloads the same action name as another projected method. Compatibility projection does not support method overloads. Use [CapabilityCompatibilityIgnore] to exclude one overload, or provide distinct method names.",
        category: "CrestCreates.Compatibility",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP035: Compatibility projection uses default route prefix 'api/'.
    /// If DynamicApiOptions.DefaultRoutePrefix is configured differently at runtime,
    /// the route will not match the legacy endpoint.
    /// </summary>
    public static readonly DiagnosticDescriptor CEP035 = new(
        id: "CEP035",
        title: "Compatibility projection uses default route prefix",
        messageFormat: "Method '{0}' uses the default route prefix 'api/'. If DynamicApiOptions.DefaultRoutePrefix is configured differently at runtime, the route will not match the legacy endpoint. Set RoutePrefix on [CapabilityCompatibilityProjection] to ensure contract fidelity.",
        category: "CrestCreates.Compatibility",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP036: [CapabilityCompatibilityProjection] on a method sets CapabilityIdPrefix or RoutePrefix.
    /// These properties are service-level concepts and should only be set on class-level attributes.
    /// Method-level override is not supported and will be ignored.
    /// </summary>
    public static readonly DiagnosticDescriptor CEP036 = new(
        id: "CEP036",
        title: "Method-level CapabilityIdPrefix/RoutePrefix override is not supported",
        messageFormat: "Method '{0}' sets {1} on [CapabilityCompatibilityProjection], but CapabilityIdPrefix and RoutePrefix are service-level properties that only take effect on class-level attributes. The method-level value will be ignored.",
        category: "CrestCreates.Compatibility",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP037: Body parameter type does not have a parameterless constructor,
    /// which is required by the generated emptyBodyFactory.
    /// Single-dimensional arrays are accepted (emitter uses Array.Empty&lt;T&gt;()).
    /// </summary>
    public static readonly DiagnosticDescriptor CEP037 = new(
        id: "CEP037",
        title: "Compatibility body type must have a parameterless constructor",
        messageFormat: "Method '{0}' has body parameter type '{1}' which does not have a parameterless constructor. The generated emptyBodyFactory requires a parameterless constructor. Add a parameterless constructor or use [CapabilityCompatibilityIgnore] to exclude this method.",
        category: "CrestCreates.Compatibility",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
