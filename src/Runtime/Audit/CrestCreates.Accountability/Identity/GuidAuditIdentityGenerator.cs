using CrestCreates.Accountability.Abstractions.Identity;

namespace CrestCreates.Accountability.Identity;

public sealed class GuidAuditIdentityGenerator : IAuditIdentityGenerator
{
    public string CreateOperationId() => Guid.NewGuid().ToString("N");

    public string CreateAuditId() => Guid.NewGuid().ToString("N");
}
