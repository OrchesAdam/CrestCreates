namespace CrestCreates.AuditLogging.Abstractions.Http;

/// <summary>
/// First-party middleware may set this feature when a request is explicitly rejected
/// by a typed authorization, validation, or governance rule.
/// </summary>
public interface IAccountabilityHttpRejectionFeature
{
    string Code { get; }
}
