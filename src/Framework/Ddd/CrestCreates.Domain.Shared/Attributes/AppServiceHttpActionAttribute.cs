using System;

namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// Declares the wire shape of one action of a hand-written projection. The attribute carries data
/// only; verbs, kinds and body policies are framework-neutral enums and the contract method is the
/// only method source (#129 rev.2 §4.1). In interface-contract mode it customizes a contract method;
/// in self-contract mode (ContractType is the service class) only public instance methods carrying
/// this attribute are projected, and unmarked helper methods are never exposed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AppServiceHttpActionAttribute : Attribute
{
    /// <summary>Not declared; the wire convention derives the verb.</summary>
    public HttpProjectionVerb Verb { get; init; } = HttpProjectionVerb.Unspecified;

    /// <summary>Route relative to the projection route prefix. Null derives it from the method name.</summary>
    public string? Route { get; init; }

    /// <summary>Not declared; the verb decides it.</summary>
    public HttpProjectionOperationKind Kind { get; init; } = HttpProjectionOperationKind.Unspecified;

    /// <summary>Permissions the action requires. Empty means "not declared here".</summary>
    public string[] Permissions { get; init; } = Array.Empty<string>();

    /// <summary>Not declared; a complex non-CT parameter derives a required body.</summary>
    public HttpProjectionBodyPolicy Body { get; init; } = HttpProjectionBodyPolicy.Unspecified;

    /// <summary>True when the action binds the concurrency token from the If-Match header.</summary>
    public bool RequiresConcurrencyToken { get; init; }

    /// <summary>
    /// True when the request body must be validated by its declared closed
    /// <c>IValidator&lt;TBody&gt;</c> before the business service or its unit of work is entered.
    /// The projection discovers the validator implementation at build time, registers it as a typed
    /// service, and fails the host composition when the registration or the body validator is missing.
    /// </summary>
    public bool RequiresValidator { get; init; }
}
