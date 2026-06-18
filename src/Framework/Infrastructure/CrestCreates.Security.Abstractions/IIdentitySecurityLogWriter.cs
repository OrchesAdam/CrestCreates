using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Security.Abstractions;

public interface IIdentitySecurityLogWriter
{
    Task WriteAsync(
        Guid? userId,
        string? userName,
        string? tenantId,
        string action,
        bool isSucceeded,
        string? detail = null,
        CancellationToken cancellationToken = default);
}
