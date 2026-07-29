namespace CrestCreates.Accountability.Abstractions.Identity;

/// <summary>
/// Supplies stable identities for accountability operations and facts.
/// </summary>
public interface IAuditIdentityGenerator
{
    string CreateOperationId();

    string CreateAuditId();
}
