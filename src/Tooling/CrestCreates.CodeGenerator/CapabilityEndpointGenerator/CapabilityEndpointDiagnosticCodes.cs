// Analyzer package intentionally avoids referencing Core.Abstractions (netstandard2.0 target).
// Diagnostic ids are const-only here because Roslyn DiagnosticDescriptor requires string ids.
namespace CrestCreates.CodeGenerator.CapabilityEndpointGenerator;

public static class CapabilityEndpointDiagnosticCodes
{
    // Warnings range
    public const string DuplicateEndpointVersionValue = "CEP1001";
    public const string UnsupportedInputTypeValue = "CEP1002";
    public const string MultipleBodyInputsValue = "CEP1003";

    // Errors range
    public const string MissingCapabilityIdValue = "CEP2001";
    public const string InvalidRoutePatternValue = "CEP2002";
    public const string EnumResolutionFailedValue = "CEP2003";

    // --- Syntax / semantic diagnostic codes (CEP001-CEP011) ---

    // Level 1 [CapabilityEndpointSpec] diagnostics
    public const string SpecMustBeSealedNestedValue = "CEP001";
    public const string ContainerMustHaveSpecsMarkerValue = "CEP002";
    public const string SpecNoMethodsOrCtorParamsValue = "CEP003";
    public const string SpecNotInsideCrestServiceValue = "CEP004";
    public const string SpecNoDynamicApiRouteValue = "CEP005";

    // CEP006-CEP007 reserved

    // Level 2 HTTP method attribute diagnostics
    public const string RouteBodyDtoMissingPropertyValue = "CEP008";
    public const string SetMustBeStaticPartialValue = "CEP009";
    public const string HttpMethodAttributeMustBeSealedPartialNestedValue = "CEP010";
    public const string PostPutPatchMissingBodyValue = "CEP011";
    public const string UnsupportedRouteParamTypeValue = "CEP012";
    public const string MultipleRouteParamsWithoutBodyValue = "CEP013";
    public const string InvalidScalarPropertyNameValue = "CEP014";
}
