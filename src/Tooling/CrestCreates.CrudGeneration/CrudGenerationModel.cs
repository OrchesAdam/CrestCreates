#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.Tooling.CrudGeneration;

/// <summary>
/// #128 S2: the single frozen CRUD generation model. The analyzer and the build-time precompile
/// stage compile this same implementation, so field rules, DTO names, routes, permissions, capability
/// identities and the compatibility projection shape are defined exactly once. The model is pure
/// data (no Roslyn types) so it can also be hashed and written into the provenance manifest.
/// </summary>
public sealed record CrudGenerationModel
{
    /// <summary>Schema version of this model; part of the incremental fingerprint.</summary>
    public const string ModelSchemaVersion = "crud-model-v1";

    /// <summary>Owner identity per §5.1: assembly + declaring service full metadata name.</summary>
    public required string OwnerAssemblyName { get; init; }

    public required string ServiceNamespace { get; init; }

    public required string ServiceClassName { get; init; }

    public string ServiceFullName => ServiceNamespace + "." + ServiceClassName;

    public required string EntityClassName { get; init; }

    public required string EntityNamespace { get; init; }

    /// <summary>Fully qualified entity display used in emitted source (global::...). </summary>
    public required string EntityQualifiedDisplay { get; init; }

    /// <summary>Id type display used in emitted source, from IEntity&lt;TId&gt; or the Id member.</summary>
    public required string IdTypeDisplay { get; init; }

    /// <summary>
    /// Wire kind of the route identity (§7.4). Frozen from the real symbol so the generated route
    /// parser is a bounded, static set of typed parse calls; unsupported identities are rejected when
    /// the model is built.
    /// </summary>
    public required CrudIdWireKind IdWireKind { get; init; }

    /// <summary>Default is the declaring service namespace plus ".Contracts"; DtoNamespace overrides.</summary>
    public required string ContractsNamespace { get; init; }

    public required string RoutePrefix { get; init; }

    public required string CapabilityIdPrefix { get; init; }

    public required bool EntityHasConcurrencyStamp { get; init; }

    public required bool EntityHasTenantId { get; init; }

    public required bool SupportsPermissionNames { get; init; }

    /// <summary>Fields of the read DTO, in declaration order.</summary>
    public required IReadOnlyList<CrudFieldModel> ReadFields { get; init; }

    /// <summary>Fields written from the create body (Id/audit/soft-delete/concurrency/tenant excluded).</summary>
    public required IReadOnlyList<CrudFieldModel> CreateFields { get; init; }

    /// <summary>Fields written from the update body; the concurrency stamp stays on the contract.</summary>
    public required IReadOnlyList<CrudFieldModel> UpdateFields { get; init; }

    /// <summary>Fields the generated query adapter may filter/sort on.</summary>
    public required IReadOnlyList<string> QueryableFields { get; init; }

    /// <summary>The five declared actions with their wire and permission shape (§7.4).</summary>
    public required IReadOnlyList<CrudActionModel> Actions { get; init; }

    /// <summary>Source location of the declaring attribute, carried into the artifacts manifest.</summary>
    public required CrudSourceLocation DeclarationLocation { get; init; }

    public string ReadDtoName => EntityClassName + "Dto";

    public string CreateDtoName => "Create" + EntityClassName + "Dto";

    public string UpdateDtoName => "Update" + EntityClassName + "Dto";

    public string ListRequestDtoName => EntityClassName + "ListRequestDto";

    public string ServiceInterfaceName => "I" + ServiceClassName;

    public string PermissionsClassName => EntityClassName + "CrudPermissions";

    public string MappingClassName => EntityClassName + "ObjectMappings";

    public string ReadDtoFullName => ContractsNamespace + "." + ReadDtoName;

    public string CreateDtoFullName => ContractsNamespace + "." + CreateDtoName;

    public string UpdateDtoFullName => ContractsNamespace + "." + UpdateDtoName;

    public string ListRequestFullName => ContractsNamespace + "." + ListRequestDtoName;

    public string InterfaceNamespace => ServiceNamespace + ".Services";

    /// <summary>The implementation partial shares the declaring service namespace, so it merges with the authored partial class.</summary>
    public string ImplementationNamespace => ServiceNamespace;

    public string PermissionsNamespace => ServiceNamespace + ".Permissions";

    public string MappingsNamespace => ServiceNamespace + ".Mappings";

    /// <summary>
    /// Capability input envelopes (§7.1): the composed route/body/header model per write action, in
    /// the shared compatibility-input namespace and naming rule.
    /// </summary>
    public string EnvelopeNamespace => CrudEnvelopeConvention.EnvelopeNamespace;

    public string EnvelopeName(string actionName) =>
        CrudEnvelopeConvention.EnvelopeName(ServiceClassName, actionName);

    /// <summary>
    /// §5.2: the assembly-qualified generated namespace of the explicit activation surface. The host
    /// imports only the namespaces of the assemblies it activates, so two Feature assemblies can each
    /// declare <c>AddGeneratedCrud</c> without an extension-method collision.
    /// </summary>
    public string ActivationNamespace => CrudActivationConvention.ActivationNamespace(OwnerAssemblyName);

    /// <summary>Type name of this service's ExplicitHost contribution (assembly-internal, unique per service).</summary>
    public string ContributionTypeName => CrudActivationConvention.ContributionTypeName(ServiceFullName);

    /// <summary>Fully-qualified name of this service's contribution, used by the generated activation.</summary>
    public string ContributionFullName => ActivationNamespace + "." + ContributionTypeName;

    /// <summary>Full name of the composed capability input envelope of one action (§7.1).</summary>
    public string EnvelopeFullName(string actionName) =>
        CrudEnvelopeConvention.EnvelopeFullName(ServiceClassName, actionName);
}

/// <summary>
/// A field that participates in the generated wire DTOs. Type text is pre-rendered by the model
/// builder because the emitters run without a Roslyn compilation (build-time transport).
/// </summary>
public sealed record CrudFieldModel
{
    public required string Name { get; init; }

    /// <summary>Nullable-annotated display used verbatim in emitted declarations.</summary>
    public required string TypeDisplay { get; init; }

    /// <summary>True for a non-nullable reference-type string, which carries the required attributes.</summary>
    public required bool IsRequiredString { get; init; }
}

/// <summary>One CRUD action of the wire matrix (§7.4), shared by projection emitters.</summary>
public sealed record CrudActionModel
{
    public required string Name { get; init; }

    public required string HttpMethod { get; init; }

    public required string RoutePattern { get; init; }

    public required string PermissionName { get; init; }

    public required string CapabilityId { get; init; }

    /// <summary>Create/Update/Delete are commands; Get/Search are queries.</summary>
    public required CrudOperationKind OperationKind { get; init; }

    /// <summary>AppService method the invoker calls; part of the wire matrix, never re-derived.</summary>
    public required string ServiceMethodName { get; init; }

    /// <summary>Body payload type full name when the action binds a required body.</summary>
    public string? BodyPayloadFullName { get; init; }

    /// <summary>Route identity member name, when the action carries one.</summary>
    public string? RouteIdentityParameter { get; init; }

    /// <summary>True when the action composes a capability input envelope instead of a bare payload.</summary>
    public bool UsesCapabilityInputEnvelope { get; init; }

    /// <summary>
    /// HTTP header inputs the action composes into its capability input (§7.4: Delete carries the
    /// If-Match concurrency header). Empty when the action has no header input.
    /// </summary>
    public IReadOnlyList<CrudHeaderInputModel> HeaderInputs { get; init; } = [];

    /// <summary>Result payload full name; null for the void/404 envelope.</summary>
    public string? ResultPayloadFullName { get; init; }

    /// <summary>True when the result payload travels inside the bounded paged envelope.</summary>
    public bool UsesPagedResult { get; init; }

    /// <summary>
    /// Capability endpoint identity, using the shared compatibility projection rule
    /// (<c>endpoint:{capabilityId}</c>) so CRUD exposes one endpoint identity authority.
    /// </summary>
    public string EndpointId => CrudWireConvention.EndpointId(CapabilityId);
}

/// <summary>
/// One HTTP header input of a CRUD action. The header joins the composed capability input envelope, so
/// the envelope member name, the wire header name and the required flag all live in the model.
/// </summary>
public sealed record CrudHeaderInputModel(string MemberName, string HeaderName, bool Required, bool TrimQuotes);

/// <summary>
/// Id wire kind of the entity identity. The build-time model freezes it from the real symbol so the
/// generated route parser is a static, bounded set of <c>TryParse</c> calls instead of runtime parsing.
/// </summary>
public enum CrudIdWireKind
{
    Guid,
    String,
    Int16,
    Int32,
    Int64,
    UInt16,
    UInt32,
    UInt64,
    Byte,
    SByte,
}


public enum CrudOperationKind
{
    Query,
    Command,
}

/// <summary>Declaration location carried into inputs/outputs manifests (§6.2 provenance).</summary>
public sealed record CrudSourceLocation(string Path, int Line, int Column)
{
    public override string ToString() => Path + "(" + Line + "," + Column + ")";
}

/// <summary>
/// A diagnostic produced while building the model. Kept free of Roslyn types so the same rules can be
/// reported by the analyzer (as Diagnostic) and by the build stage (as MSBuild error/tool diagnostic).
/// </summary>
public sealed record CrudGenerationDiagnostic(
    string Id,
    string Message,
    CrudSourceLocation? Location,
    IReadOnlyList<string> TypeArguments)
{
    public CrudGenerationDiagnostic(string id, string message, CrudSourceLocation? location)
        : this(id, message, location, Array.Empty<string>())
    {
    }

    public string ToDisplayString() =>
        Id + ": " + Message + (Location is null ? string.Empty : " at " + Location);
}

/// <summary>
/// Naming/ownership rule for composed capability input envelopes. One definition shared by the CRUD
/// producer and the existing compatibility projection, so a second naming rule cannot appear.
/// </summary>
public static class CrudEnvelopeConvention
{
    public const string EnvelopeNamespace = "CrestCreates.Generated";

    public const string Suffix = "_CompatibilityInput";

    /// <summary>Envelope member the route identity binds to (§7.4 route Id).</summary>
    public const string RouteIdentityMember = "Id";

    /// <summary>Envelope member the update body binds to.</summary>
    public const string BodyMember = "Input";

    public static string EnvelopeName(string serviceClassName, string actionName) =>
        serviceClassName + "_" + actionName + Suffix;

    public static string EnvelopeFullName(string serviceClassName, string actionName) =>
        EnvelopeNamespace + "." + EnvelopeName(serviceClassName, actionName);
}

/// <summary>
/// Wire identities the formal CRUD surface shares with the existing compatibility projection. The
/// endpoint id rule is the projection's (one capability gets one <c>endpoint:{capabilityId}</c>), and
/// the concurrency header is the one the projection already reads from the composed stamp parameter.
/// </summary>
public static class CrudWireConvention
{
    public const string EndpointIdPrefix = "endpoint:";

    public const string ConcurrencyHeaderName = "If-Match";

    public static string EndpointId(string capabilityId) => EndpointIdPrefix + capabilityId;
}

/// <summary>
/// §5.2: the ExplicitHost activation surface lives in a per-assembly generated namespace, and the
/// contribution type name is unique per service inside that assembly. Both are pure functions of the
/// owner identity, so the producer and any consumer derive the same names.
/// </summary>
public static class CrudActivationConvention
{
    public const string NamespaceRoot = "CrestCreates.Generated";

    public const string ActivationTypeName = "GeneratedCrudActivation";

    public const string ActivationMethodName = "AddGeneratedCrud";

    public const string ContributionPrefix = "GeneratedCrudContribution_";

    public static string ActivationNamespace(string assemblyName) =>
        NamespaceRoot + "." + SanitizeIdentifier(assemblyName);

    public static string ContributionTypeName(string serviceFullName) =>
        ContributionPrefix + SanitizeIdentifier(serviceFullName);

    /// <summary>Turns arbitrary identity text into one deterministic C# identifier.</summary>
    public static string SanitizeIdentifier(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 1);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var valid = character == '_'
                || (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (index > 0 && character >= '0' && character <= '9');
            builder.Append(valid ? character : '_');
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }
}

/// <summary>Result/DTO type names of the response side, derived from the same model (§7.1 roots).</summary>
public static class CrudResponseContract
{
    public const string DynamicApiEnvelopeNamespace = "CrestCreates.DynamicApi";

    public const string PagedResultNamespace = "CrestCreates.Application.Contracts.DTOs.Common";

    /// <summary>
    /// Envelope over an already-rendered payload display (the payload may itself be generic, e.g.
    /// <c>PagedResultDto&lt;Dto&gt;</c>, so a second <c>global::</c> prefix must not be added). This is
    /// the one envelope rule for every typed result mapper.
    /// </summary>
    public static string EnvelopeOfPayload(string payloadDisplay) =>
        "global::" + DynamicApiEnvelopeNamespace + ".DynamicApiResponse<" + payloadDisplay + ">";

    public static string PagedPayload(string dtoFullName) =>
        "global::" + PagedResultNamespace + ".PagedResultDto<global::" + dtoFullName + ">";

    public static string VoidEnvelope =>
        "global::" + DynamicApiEnvelopeNamespace + ".DynamicApiResponse";
}

/// <summary>Model plus the diagnostics that came out of the same build pass.</summary>
public sealed record CrudModelBuildResult(
    IReadOnlyList<CrudGenerationModel> Models,
    IReadOnlyList<CrudGenerationDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Id != CrudDiagnosticIds.Info);
}
