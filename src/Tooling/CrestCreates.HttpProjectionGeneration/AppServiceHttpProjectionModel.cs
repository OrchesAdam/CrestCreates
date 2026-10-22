#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CrestCreates.Tooling.HttpProjection;

/// <summary>
/// Issue #129 S2: the frozen HTTP projection model for hand-written application services. Pure data
/// (no Roslyn types) so the precompile front-end, the emitter and the manifest can share one shape.
/// The CRUD model keeps owning its own DTO/service generation; this model only describes the wire.
/// </summary>
public sealed record AppServiceHttpProjectionModel
{
    public const string ModelSchemaVersion = "appservice-http-model-v2";

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

    /// <summary>All contract parameters in declaration order, including the CancellationToken.</summary>
    public required IReadOnlyList<AppServiceHttpBoundParameter> Parameters { get; init; }

    public string? BodyTypeFullName { get; init; }

    public string? ResultTypeFullName { get; init; }

    /// <summary>How the contract method returns: sync, Task, ValueTask, or void.</summary>
    public required AppServiceHttpResultKind ResultKind { get; init; }

    public bool ResultIsValueType { get; init; }

    /// <summary>Route identity member (for example "id" or "customerId").</summary>
    public string? RouteIdentityParameterName { get; init; }

    /// <summary>Scalar route/query parameters bound individually.</summary>
    public required IReadOnlyList<AppServiceHttpInputParameter> ScalarInputs { get; init; }

    /// <summary>Composed input envelope when the action has more than one non-CT input.</summary>
    public string? InputEnvelopeFullName { get; init; }

    public bool UsesCapabilityInputEnvelope => InputEnvelopeFullName is not null;

    public required bool RequiresConcurrencyToken { get; init; }

    public bool RequiresValidator { get; init; }

    /// <summary>
    /// Closed validator implementation discovered for the body type (FluentValidation
    /// <c>AbstractValidator&lt;TBody&gt;</c> / <c>IValidator&lt;TBody&gt;</c>). The assembly activation
    /// registers it as a typed service and <see cref="RequiresValidator"/> actions execute it before
    /// the business service.
    /// </summary>
    public string? ValidatorImplementationFullName { get; init; }

    /// <summary>Closed <c>IValidator&lt;TBody&gt;</c> display the host composition must be able to resolve.</summary>
    public string? ValidatorContractFullName { get; init; }
}

/// <summary>One contract parameter and how the HTTP wire binds it, in contract order.</summary>
public sealed record AppServiceHttpBoundParameter
{
    public required string Name { get; init; }

    public required string TypeFullName { get; init; }

    /// <summary>CLR display used in <c>typeof</c>: nullable reference annotations stripped, Nullable&lt;T&gt; kept.</summary>
    public string? ClrTypeFullName { get; init; }

    /// <summary>"Route", "Query", "Body", "Header" or "CancellationToken".</summary>
    public required string Source { get; init; }

    public required bool Required { get; init; }

    public required AppServiceHttpScalarKind ScalarKind { get; init; }

    /// <summary>Envelope member name for this parameter (PascalCase, or Id/Input conventions).</summary>
    public required string MemberName { get; init; }

    /// <summary>False for a synthetic header input that has no matching contract parameter.</summary>
    public bool IsContractParameter { get; init; } = true;
}

public enum AppServiceHttpScalarKind
{
    NotScalar = 0,
    Guid,
    String,
    Int16,
    UInt16,
    Int32,
    Int64,
    UInt32,
    UInt64,
    Byte,
    SByte,
    Boolean,
    Decimal,
    Double,
    Single,
    Char,
    DateTime,
    DateTimeOffset,
    Enum,
    Unsupported,
}

public enum AppServiceHttpResultKind
{
    Void,
    SyncValue,
    TaskValue,
    ValueTaskValue,
    TaskVoid,
    ValueTaskVoid,
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

    /// <summary>
    /// Exact symbols of the body, input and payload roots of every action. The JSON stage resolves
    /// its roots from these symbols instead of re-parsing display text (S22B-R1-01).
    /// </summary>
    public IReadOnlyList<AppServiceHttpRootSymbol> RootSymbols { get; init; } =
        Array.Empty<AppServiceHttpRootSymbol>();
}

/// <summary>One root-bearing symbol of an action, with the display the wire planner records.</summary>
public sealed record AppServiceHttpRootSymbol(
    string Action,
    string Component,
    string Display,
    ITypeSymbol Type);

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
