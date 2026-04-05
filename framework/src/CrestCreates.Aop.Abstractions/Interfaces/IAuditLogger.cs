using System.Threading.Tasks;

namespace CrestCreates.Aop.Abstractions.Interfaces;

public interface IAuditLogger
{
    Task LogAsync(string actionName, string? entityType, string? entityId, string? description, object? parameters = null, object? result = null);
    Task LogExceptionAsync(string actionName, string? entityType, string? entityId, string? description, System.Exception exception);
}
