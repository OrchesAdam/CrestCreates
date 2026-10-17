#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.Tooling.HttpProjection;

/// <summary>
/// Issue #129 S2: the frozen HTTP projection model for hand-written application services. Pure data
/// (no Roslyn types) so the precompile front-end, the emitter and the manifest can share one shape.
/// The CRUD model keeps owning its own DTO/service generation; this model only describes the wire.
/// </summary>
public sealed record AppServiceHttpProjectionModel
{
    public const string ModelSchemaVersion = "appservice-http-model-v1";

    public const string ActivationMethodName = "AddGeneratedAppServiceHttp";

    /// <summary>Assembly that owns the generated HTTP glue (where the anchor declaration lives).</summary>
    public required string HttpOwnerAssemblyName { get; init; }

    public required string AnchorTypeFullName { get; init; }

    /// <summary>Assembly that defines <see cref="ContractFullName"/> (may be a referenced assembly).</summary>
    public required string ContractAssemblyName { get; init; }

    public required string ContractFullName { get; init; }

    public required string ContractNamespace { get; init; }

    public required string ContractName { get; init; }

    public required string RoutePrefix { get; init; }

    public required string CapabilityIdPrefix { get; init; }

    public required AppServiceHttpRegistrationMode RegistrationMode { get; init; }

    public required IReadOnlyList<AppServiceHttpActionModel> Actions { get; init; }

    /// <summary>
    /// Types the projection depends on that are defined outside the HTTP owner. They are recorded
    /// separately from the artifact's declared types (#129 addendum §3.3); the manifest must never
    /// mix them into DeclaredTypeFullNames.
    /// </summary>
    public required IReadOnlyList<AppServiceHttpReferenceIdentity> ReferencedTypes { get; init; }

    public required string DeclarationLocation { get; init; }

    public IReadOnlyList<string> RequiredJsonRootTypeNames => Actions
        .SelectMany(action => new[] { action.BodyTypeFullName, action.ResultTypeFullName, action.InputEnvelopeFullName })
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList()!;
}

/// <summary>Wire shape of one contract action. Frozen from symbols; never re-derived by an emitter.</summary>
public sealed record AppServiceHttpActionModel
{
    public required string Name { get; init; }

    public required string HttpMethod { get; init; }

    public required string RoutePattern { get; init; }

    public required AppServiceHttpOperationKind OperationKind { get; init; }

    public required AppServiceHttpBodyPolicy BodyPolicy { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }

    public required string CapabilityId { get; init; }

    public required string EndpointId { get; init; }

    /// <summary>Closed contract method signature: name + parameter displays + return display.</summary>
    public required string ClosedContractMethodSignature { get; init; }

    public string? CancellationTokenParameterName { get; init; }

    public string? BodyTypeFullName { get; init; }

    public string? ResultTypeFullName { get; init; }

    /// <summary>Route identity member (for example "id" or "customerId").</summary>
    public string? RouteIdentityParameterName { get; init; }

    /// <summary>Scalar route/query parameters bound individually.</summary>
    public required IReadOnlyList<AppServiceHttpInputParameter> ScalarInputs { get; init; }

    /// <summary>Composed input envelope when the action has more than one non-CT input.</summary>
    public string? InputEnvelopeFullName { get; init; }

    public bool UsesCapabilityInputEnvelope => InputEnvelopeFullName is not null;

    public required bool RequiresConcurrencyToken { get; init; }
}

public sealed record AppServiceHttpInputParameter
{
    public required string Name { get; init; }

    /// <summary>Full display of the parameter type, with global:: prefix.</summary>
    public required string TypeFullName { get; init; }

    /// <summary>"route" or "query".</summary>
    public required string Binding { get; init; }
}

/// <summary>Identity of a type the projection depends on, defined outside the HTTP owner.</summary>
public sealed record AppServiceHttpReferenceIdentity
{
    public required string AssemblyName { get; init; }

    public required string FullMetadataName { get; init; }

    public required bool IsContract { get; init; }
}

public enum AppServiceHttpOperationKind
{
    Query = 1,
    Command = 2,
}

public enum AppServiceHttpBodyPolicy
{
    None = 1,
    Required = 2,
    Optional = 3,
}

public enum AppServiceHttpRegistrationMode
{
    ExplicitHost = 0,
    LegacyStatic = 1,
}

public enum AppServiceHttpDiagnosticSeverity
{
    Warning = 0,
    Error = 1,
}

public sealed record AppServiceHttpProjectionDiagnostic(
    string Id,
    string Message,
    AppServiceHttpDiagnosticSeverity Severity,
    string? Member)
{
    public string ToDisplayString() => Id + ": " + Message + (Member is null ? string.Empty : " at " + Member);
}

public sealed record AppServiceHttpProjectionBuildResult(
    AppServiceHttpProjectionModel? Model,
    IReadOnlyList<AppServiceHttpProjectionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == AppServiceHttpDiagnosticSeverity.Error);
}

/// <summary>
/// Issue #129 rev.2 §9: CCHTTP001-008. Declared here so the analyzer and the precompile front-end
/// cannot drift apart.
/// </summary>
public static class AppServiceHttpDiagnosticCodes
{
    public const string UnsupportedDeclaration = "CCHTTP001";
    public const string NotMaterialized = "CCHTTP002";
    public const string OwnerRouteActionConflict = "CCHTTP003";
    public const string JsonClosureIncomplete = "CCHTTP004";
    public const string FormalUsesLegacyOrDoublePublish = "CCHTTP005";
    public const string TransportManifestState = "CCHTTP006";
    public const string RequiredHostComposition = "CCHTTP007";
    public const string ActionDeclarationContradiction = "CCHTTP008";
}
