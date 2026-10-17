namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>HTTP verb of one projected action. Declaration data only; no ASP.NET dependency.</summary>
public enum HttpProjectionVerb
{
    /// <summary>Not declared; the wire convention derives it.</summary>
    Unspecified = 0,
    Get = 1,
    Post = 2,
    Put = 3,
    Patch = 4,
    Delete = 5,
}
