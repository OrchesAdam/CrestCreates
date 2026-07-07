using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.CapabilityEndpointGenerator;

/// <summary>
/// Diagnostic descriptors for CapabilityEndpoint attribute usage validation.
/// Follows the same pattern as CanonicalHashDiagnostics and AgentDraftContractDiagnostics.
/// </summary>
internal static class CapabilityEndpointDiagnostics
{
    private const string Category = "CapabilityEndpoint";

    // --- CEP001-005: Level 1 [CapabilityEndpointSpec] diagnostics ---

    /// <summary>
    /// CEP001: [CapabilityEndpointSpec] must be on a sealed nested class.
    /// </summary>
    public static readonly DiagnosticDescriptor SpecMustBeSealedNested = new(
        id: CapabilityEndpointDiagnosticCodes.SpecMustBeSealedNestedValue,
        title: "[CapabilityEndpointSpec] must be on a sealed nested class",
        messageFormat: "[CapabilityEndpointSpec] on class '{0}' requires the class to be both sealed and nested inside a container class. Add 'sealed' modifier and ensure it is declared inside a [CapabilityEndpointSpecs]-marked container.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP002: Container class must have [CapabilityEndpointSpecs] marker.
    /// </summary>
    public static readonly DiagnosticDescriptor ContainerMustHaveSpecsMarker = new(
        id: CapabilityEndpointDiagnosticCodes.ContainerMustHaveSpecsMarkerValue,
        title: "Container class must have [CapabilityEndpointSpecs] marker",
        messageFormat: "Container class '{0}' must be marked with [CapabilityEndpointSpecs] when it contains [CapabilityEndpointSpec] nested classes.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP003: Spec class cannot have methods or constructors with parameters.
    /// </summary>
    public static readonly DiagnosticDescriptor SpecNoMethodsOrCtorParams = new(
        id: CapabilityEndpointDiagnosticCodes.SpecNoMethodsOrCtorParamsValue,
        title: "Spec class cannot have methods or constructors with parameters",
        messageFormat: "[CapabilityEndpointSpec] class '{0}' cannot declare methods or constructors with parameters. Spec classes are attribute-only declarations.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP004: Spec class cannot be inside a [CrestService] type.
    /// </summary>
    public static readonly DiagnosticDescriptor SpecNotInsideCrestService = new(
        id: CapabilityEndpointDiagnosticCodes.SpecNotInsideCrestServiceValue,
        title: "Spec class cannot be inside a [CrestService] type",
        messageFormat: "[CapabilityEndpointSpec] class '{0}' is nested inside a [CrestService] type '{1}'. Spec classes must be in dedicated container classes.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP005: Spec class cannot coexist with [DynamicApiRoute].
    /// </summary>
    public static readonly DiagnosticDescriptor SpecNoDynamicApiRoute = new(
        id: CapabilityEndpointDiagnosticCodes.SpecNoDynamicApiRouteValue,
        title: "Spec class cannot coexist with [DynamicApiRoute]",
        messageFormat: "[CapabilityEndpointSpec] class '{0}' cannot also be decorated with [DynamicApiRoute]. Choose one endpoint projection mechanism.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // CEP006-CEP007 reserved for future diagnostics.

    // --- CEP008-011: Level 2 HTTP method attribute diagnostics ---

    /// <summary>
    /// CEP008: Route+Body DTO must have a settable property matching each route token name.
    /// </summary>
    public static readonly DiagnosticDescriptor RouteBodyDtoMissingProperty = new(
        id: CapabilityEndpointDiagnosticCodes.RouteBodyDtoMissingPropertyValue,
        title: "Route+Body DTO missing settable property for route token",
        messageFormat: "Body type '{0}' is missing settable properties for route tokens: {1}. Add public settable properties with names matching the route template tokens.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP009: [CapabilityEndpointSet] must be on a static partial class.
    /// </summary>
    public static readonly DiagnosticDescriptor SetMustBeStaticPartial = new(
        id: CapabilityEndpointDiagnosticCodes.SetMustBeStaticPartialValue,
        title: "[CapabilityEndpointSet] must be on a static partial class",
        messageFormat: "[CapabilityEndpointSet] on class '{0}' requires the class to be 'static partial'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP010: HTTP method attribute ([Post]/[Get]/etc.) must be on a sealed partial nested class.
    /// </summary>
    public static readonly DiagnosticDescriptor HttpMethodAttributeMustBeSealedPartialNested = new(
        id: CapabilityEndpointDiagnosticCodes.HttpMethodAttributeMustBeSealedPartialNestedValue,
        title: "HTTP method attribute must be on a sealed partial nested class",
        messageFormat: "HTTP method attribute on class '{0}' requires the class to be 'sealed partial' and nested inside a [CapabilityEndpointSet] container.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP011: [Post]/[Put]/[Patch] without Body property is likely an error.
    /// Warning severity — may be intentional for capabilities that don't need a body.
    /// </summary>
    public static readonly DiagnosticDescriptor PostPutPatchMissingBody = new(
        id: CapabilityEndpointDiagnosticCodes.PostPutPatchMissingBodyValue,
        title: "[Post]/[Put]/[Patch] without Body property",
        messageFormat: "HTTP method attribute on class '{0}' is [Post], [Put], or [Patch] but does not specify a Body. This is likely an error; add Body = typeof(...) if the endpoint expects a request body.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP012: Route parameter type is neither a known scalar nor an enum.
    /// Only string, numeric, Guid, DateTime, DateTimeOffset, bool and enum types
    /// can be used as route/query binding targets in 8a.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedRouteParamType = new(
        id: CapabilityEndpointDiagnosticCodes.UnsupportedRouteParamTypeValue,
        title: "Unsupported route parameter type",
        messageFormat: "Route parameter '{0}' on endpoint spec '{1}' has unsupported type '{2}'. Only scalar types (string, Guid, int, long, bool, etc.) and enum types are supported for route binding in 8a.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP013: Multiple route tokens detected without a body or input type.
    /// In 8a the generator creates a dictionary binding, but a Body/Input type is preferred.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleRouteParamsWithoutBody = new(
        id: CapabilityEndpointDiagnosticCodes.MultipleRouteParamsWithoutBodyValue,
        title: "Multiple route parameters without a body type",
        messageFormat: "Endpoint spec '{0}' declares {1} route tokens but no Body or Input type. Route-only binding with multiple parameters generates a Dictionary<string, object?> in 8a. Consider adding a Body or Input type for strong-typed binding.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP014: Input Name is not a valid C# identifier, which will produce
    /// invalid property assignment code in body+scalar binding emission.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidScalarPropertyName = new(
        id: CapabilityEndpointDiagnosticCodes.InvalidScalarPropertyNameValue,
        title: "Invalid property name for scalar-to-body binding",
        messageFormat: "Input '{0}' on endpoint spec '{1}' has Name '{2}' which is not a valid C# identifier. Specify CapabilityInputPath to provide the target property name.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP015: Body binding uses generic ReadBodyAsync which is not fully AOT-safe.
    /// </summary>
    public static readonly DiagnosticDescriptor BodyBindingNotAotSafe = new(
        id: CapabilityEndpointDiagnosticCodes.BodyBindingNotAotSafeValue,
        title: "Body binding uses generic ReadBodyAsync — not fully AOT-safe",
        messageFormat: "Endpoint spec '{0}' uses body binding via generic ReadBodyAsync<{1}>. For full AOT safety, provide a JsonSerializerContext with [JsonSerializable] for the body type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// CEP016: HTTP method attribute must be nested inside a [CapabilityEndpointSet] container.
    /// </summary>
    public static readonly DiagnosticDescriptor HttpMethodAttributeMustBeInsideCapabilityEndpointSet = new(
        id: CapabilityEndpointDiagnosticCodes.HttpMethodAttributeMustBeInsideCapabilityEndpointSetValue,
        title: "HTTP method attribute must be inside a [CapabilityEndpointSet] container",
        messageFormat: "[{0}] on class '{1}' must be nested inside a [CapabilityEndpointSet]-marked container class. Level 2 HTTP method attributes require a [CapabilityEndpointSet] container to provide default RoutePrefix, GroupName, and Tags.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Helper to create a <see cref="Diagnostic"/> from a descriptor.
    /// </summary>
    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location? location, params object[] args)
    {
        return Diagnostic.Create(descriptor, location, args);
    }
}
