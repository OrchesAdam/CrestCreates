using System;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Entities;
using CrestCreates.AuditLogging.Services;
using CrestCreates.Authorization.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.AuditLogging.Interceptors;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class AuditedMoAttribute : AsyncMoAttribute
{
    private readonly string? _actionName;
    private readonly bool _includeParameters;
    private readonly bool _includeResult;
    private DateTime _startTime;

    public AuditedMoAttribute(string? actionName = null, bool includeParameters = true, bool includeResult = false)
    {
        _actionName = actionName;
        _includeParameters = includeParameters;
        _includeResult = includeResult;
    }

    public override ValueTask OnEntryAsync(MethodContext context)
    {
        try
        {
            _startTime = DateTime.UtcNow;
            return ValueTask.CompletedTask;
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }
    }

    public override async ValueTask OnSuccessAsync(MethodContext context)
    {
        var auditLogger = context.GetService<IAuditLogService>();
        if (auditLogger == null)
        {
            return;
        }

        var actionName = _actionName ?? $"{context.Method.DeclaringType?.Name}.{context.Method.Name}";

        object? parameters = null;
        if (_includeParameters && context.Arguments.Length > 0)
        {
            try
            {
                parameters = context.Arguments.Length == 1 ? context.Arguments[0] : context.Arguments;
            }
            catch
            {
                parameters = "无法序列化参数";
            }
        }

        object? result = null;
        if (_includeResult && context.ReturnValue != null)
        {
            try
            {
                result = context.ReturnValue;
            }
            catch
            {
                result = "无法序列化结果";
            }
        }

        var duration = DateTime.UtcNow - _startTime;
        
        try
        {
            var currentUser = context.GetService<ICurrentUser>();
            var httpContext = context.GetService<IHttpContextAccessor>()?.HttpContext;
            
            var auditLog = new AuditLog
            {
                CreationTime = _startTime,
                UserId = currentUser?.Id,
                UserName = currentUser?.UserName,
                Action = actionName,
                Description = _actionName ?? $"执行方法: {context.Method.Name}",
                ClientIpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                ClientName = httpContext?.Request.Headers["User-Agent"],
                Path = httpContext?.Request.Path.ToString(),
                HttpMethod = httpContext?.Request.Method,
                StatusCode = 200,
                ExecutionTime = (long)duration.TotalMilliseconds,
                Request = parameters != null ? System.Text.Json.JsonSerializer.Serialize(parameters) : null,
                Response = result != null ? System.Text.Json.JsonSerializer.Serialize(result) : null,
                Exception = null,
                TenantId = currentUser?.TenantId,
                ExtraProperties = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "Method", $"{context.Method.DeclaringType?.FullName}.{context.Method.Name}" },
                    { "ParametersCount", context.Arguments.Length },
                    { "HasReturnValue", context.ReturnValue != null }
                }
            };
            await auditLogger.CreateAsync(auditLog);

        }
        catch (Exception ex)
        {
            var logger = context.GetService<ILogger<AuditedMoAttribute>>();
            logger?.LogWarning(ex, "审计日志记录失败");
        }
    }

    public override async ValueTask OnExceptionAsync(MethodContext context)
    {
        var auditLogger = context.GetService<IAuditLogService>();
        if (auditLogger == null) return;

        var currentUser = context.GetService<ICurrentUser>();
        var httpContext = context.GetService<IHttpContextAccessor>()?.HttpContext;
        var actionName = _actionName ?? $"{context.Method.DeclaringType?.Name}.{context.Method.Name}";
        var duration = DateTime.UtcNow - _startTime;

        try
        {
            var auditLog = new AuditLog
            {
                CreationTime = _startTime,
                UserId = currentUser?.Id,
                UserName = currentUser?.UserName,
                Action = actionName,
                Description = _actionName ?? $"执行方法: {context.Method.Name}",
                ClientIpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                ClientName = httpContext?.Request.Headers["User-Agent"],
                Path = httpContext?.Request.Path.ToString(),
                HttpMethod = httpContext?.Request.Method,
                StatusCode = 500,
                ExecutionTime = (long)duration.TotalMilliseconds,
                Request = null,
                Response = null,
                Exception = context.Exception?.ToString(),
                TenantId = currentUser?.TenantId,
                ExtraProperties = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "Method", $"{context.Method.DeclaringType?.FullName}.{context.Method.Name}" },
                    { "ExceptionType", context.Exception?.GetType().Name ?? "" },
                    { "ExceptionMessage", context.Exception?.Message ??  ""}
                }
            };
            await auditLogger.CreateAsync(auditLog);
        }
        catch (Exception ex)
        {
            var logger = context.GetService<ILogger<AuditedMoAttribute>>();
            logger?.LogWarning(ex, "审计日志记录失败");
        }
    }
}

