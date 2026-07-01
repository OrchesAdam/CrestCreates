namespace CrestCreates.Agent.Authoring.Abstractions.Model;

/// <summary>
/// Categorizes the reason a model client failed to produce a response.
/// None means the response was successful (ResponseText is non-empty).
/// </summary>
public enum DescriptorAuthoringProviderFailureKind
{
    None = 0,
    CredentialUnavailable = 1,
    CredentialRejected = 2,
    Unauthorized = 3,
    RateLimited = 4,
    Timeout = 5,
    NetworkError = 6,
    Unknown = 7
}
