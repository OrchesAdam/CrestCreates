using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.AppServiceCompatibilityGenerator;

internal sealed record CompatibilityServiceModel(
    string ServiceName,
    string StrippedName,
    string SanitizedIdentifier, // PascalCase for C# identifiers (e.g., "BookCatalog")
    string RoutePrefix,
    string CapabilityIdPrefix,
    string ServiceTypeName,
    string InterfaceTypeName,
    CompatibilityActionModel[] Actions,
    DiagnosticDescriptorAndLocation[] Diagnostics);

internal sealed record CompatibilityActionModel(
    string ActionName,
    string HttpMethod,
    string RoutePattern,
    string CapabilityId,
    string EndpointId,
    string PermissionName,
    string ServiceMethodName,
    bool IsSingleParam,        // single Body param
    bool IsSingleScalarParam,  // single Route/Query/Header param (no body)
    string InputTypeName,
    string? EnvelopeTypeName,
    string ReturnTypeName,
    bool IsVoidReturn,
    bool HasCancellationToken, // true when original method declares a CancellationToken param
    CompatibilityParameterModel[] Parameters);

/// <summary>
/// Per-parameter metadata used by endpoint and invoker emitters for
/// envelope generation, input bindings, and method-call argument construction.
/// </summary>
internal sealed record CompatibilityParameterModel(
    string Name,
    string TypeName,
    string TypeOfExpression, // Expression for typeof() and Resolve<T> — strips ? for nullable ref types, uses Nullable<T> for nullable value types
    string Source, // "Route", "Query", "Body", "Header", "CancellationToken"
    string? PascalName, // PascalCase name for envelope property access
    bool IsOptional, // true when parameter is optional or has default value
    bool IsQueryObject, // true when source=Query and type is non-scalar (complex DTO)
    string? HeaderName, // HTTP header name for Header-source params (e.g., "If-Match" for expectedStamp)
    ImmutableArray<CompatibilityQueryPropertyModel> QueryProperties);

/// <summary>
/// Represents a scalar property of a complex query DTO that should be
/// bound from individual query string values.
/// </summary>
internal sealed record CompatibilityQueryPropertyModel(
    string Name,
    string TypeName,
    bool IsScalar,
    bool IsOptional);

/// <summary>
/// Carries a diagnostic descriptor + location for deferred reporting
/// in the source output phase where SourceProductionContext is available.
/// </summary>
internal sealed record DiagnosticDescriptorAndLocation(
    DiagnosticDescriptor Descriptor,
    Location Location,
    object?[] MessageArgs);
