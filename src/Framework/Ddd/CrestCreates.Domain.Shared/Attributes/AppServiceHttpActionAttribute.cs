using System;

namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>
/// Declares the wire shape of one action of a hand-written projection. The attribute carries data
/// only; verbs, kinds and body policies are framework-neutral enums and the contract method is the
/// only method source (#129 rev.2 §4.1).
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
}
