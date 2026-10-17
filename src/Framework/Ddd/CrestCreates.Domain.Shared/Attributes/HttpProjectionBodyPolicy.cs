namespace CrestCreates.Domain.Shared.Attributes;

/// <summary>Body requirement of one projected action.</summary>
public enum HttpProjectionBodyPolicy
{
    Unspecified = 0,
    None = 1,
    Required = 2,
    Optional = 3,
}
