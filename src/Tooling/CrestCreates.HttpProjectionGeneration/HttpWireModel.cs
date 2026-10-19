#nullable enable
using System;
using System.Collections.Generic;

namespace CrestCreates.Tooling.HttpProjection;

/// <summary>
/// #129 S2.2: the neutral HTTP wire model both front-ends normalize to before emission. CRUD maps its
/// entity/action model onto it; hand-written services map their contract model onto it. The emitter only
/// ever sees this shape, so the two chains cannot carry two sets of HTTP rules.
/// </summary>
public sealed record HttpWireModel
{
    public required string OwnerAssemblyName { get; init; }

    /// <summary>Full name of the contract the generated invoker resolves from DI.</summary>
    public required string ContractFullName { get; init; }

    public required string ContributionTypeName { get; init; }

    public required string JsonContextFullName { get; init; }

    /// <summary>Name used in required-identity diagnostics (entity name for CRUD, contract for app services).</summary>
    public required string DiagnosticsTypeName { get; init; }

    public required IReadOnlyList<HttpWireAction> Actions { get; init; }

    /// <summary>Additional required JSON roots not derived from the action inputs/results (for example ProblemDetails).</summary>
    public IReadOnlyList<HttpWireJsonRoot> ExtraJsonRoots { get; init; } = Array.Empty<HttpWireJsonRoot>();

    /// <summary>
    /// True for front-ends whose binder hands a bare scalar to the capability input (hand-written
    /// services); the CRUD matrix stays the frozen eleven roots and does not add route/header scalars.
    /// </summary>
    public bool EmitInputScalarRoots { get; init; }
}

public sealed record HttpWireAction
{
    public required string Name { get; init; }

    /// <summary>Wire action name used for the generated method names (CRUD: Create/Get/...).</summary>
    public required string ContractMethodName { get; init; }

    public required string HttpMethod { get; init; }

    /// <summary>Full route pattern including the prefix.</summary>
    public required string RoutePattern { get; init; }

    public required AppServiceHttpOperationKind OperationKind { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }

    public required string CapabilityId { get; init; }

    public required string EndpointId { get; init; }

    public required IReadOnlyList<HttpWireParameter> Parameters { get; init; }

    public bool UsesEnvelope { get; init; }

    public string? EnvelopeFullName { get; init; }

    /// <summary>Ordered invoker arguments after normalization (Kind Member = envelope member, Local = local variable, Ct).</summary>
    public required IReadOnlyList<HttpWireArgument> InvokerArguments { get; init; }

    public string? BodyTypeFullName { get; init; }

    public bool BodyOptional { get; init; }

    /// <summary>Result payload display already rendered (paged wrapper applied when needed).</summary>
    public string? ResultPayloadFullName { get; init; }

    public required AppServiceHttpResultKind ResultKind { get; init; }

    public bool ResultIsValueType { get; init; }

    public bool MapNotFoundOntoNull { get; init; }

    /// <summary>Optional extra provenance for required identity diagnostics (entity name); falls back to the owner.</summary>
    public string? RequiredIdentityTypeName { get; init; }
}

public sealed record HttpWireParameter
{
    public required string Name { get; init; }

    /// <summary>Router/query/body/header binding, or CancellationToken.</summary>
    public required string Source { get; init; }

    public required string TypeFullName { get; init; }

    public required bool Required { get; init; }

    public required AppServiceHttpScalarKind ScalarKind { get; init; }

    public required string MemberName { get; init; }

    public string? HeaderName { get; init; }

    public bool TrimQuotes { get; init; }

    /// <summary>Local variable name the binder declares for this input (CRUD keeps id/body).</summary>
    public string? LocalName { get; init; }

    /// <summary>True when the emitted envelope member needs a <c>= default!</c> initializer.</summary>
    public bool NeedsDefaultInitializer { get; init; }

    /// <summary>CLR display used in <c>typeof</c>; falls back to <see cref="TypeFullName"/>.</summary>
    public string? ClrTypeFullName { get; init; }

    /// <summary>False for a synthetic header input with no matching contract parameter.</summary>
    public bool IsContractParameter { get; init; } = true;
}

public enum HttpWireArgumentKind
{
    EnvelopeMember,
    Local,
    CancellationToken,
}

public sealed record HttpWireArgument(HttpWireArgumentKind Kind, string Value);

public sealed record HttpWireJsonRoot(string TypeDisplay, string Action, string Component, string? ClrTypeDisplay = null);
