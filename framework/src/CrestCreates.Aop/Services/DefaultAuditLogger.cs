using System;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Aop.Services;

internal class DefaultAuditLogger : IAuditLogger
{
    private readonly ILogger<DefaultAuditLogger> _logger;

    public DefaultAuditLogger(ILogger<DefaultAuditLogger> logger)
    {
        _logger = logger;
    }

    public Task LogAsync(string actionName, string? entityType, string? entityId, string? description, object? parameters = null, object? result = null)
    {
        _logger.LogInformation(
            "审计日志 - 操作: {ActionName}, 实体类型: {EntityType}, 实体ID: {EntityId}, 描述: {Description}",
            actionName, entityType, entityId, description
        );
        return Task.CompletedTask;
    }

    public Task LogExceptionAsync(string actionName, string? entityType, string? entityId, string? description, Exception exception)
    {
        _logger.LogError(
            exception,
            "审计日志(异常) - 操作: {ActionName}, 实体类型: {EntityType}, 实体ID: {EntityId}, 描述: {Description}",
            actionName, entityType, entityId, description
        );
        return Task.CompletedTask;
    }
}
