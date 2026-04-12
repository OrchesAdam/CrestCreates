using System;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;
using Microsoft.Extensions.Logging;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.AuditLogging.Interceptors;

/// <summary>
/// 方法级审计拦截器 - 补充方法级别的审计数据到统一的AuditContext
/// 注意：此拦截器不再直接写入审计日志，而是丰富AuditContext后由中间件统一写入
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class AuditedMoAttribute : AsyncMoAttribute
{
    private readonly string? _actionName;
    private readonly bool _includeParameters;
    private readonly bool _includeResult;

    public AuditedMoAttribute(string? actionName = null, bool includeParameters = true, bool includeResult = false)
    {
        _actionName = actionName;
        _includeParameters = includeParameters;
        _includeResult = includeResult;
    }

    public override ValueTask OnEntryAsync(MethodContext context)
    {
        // Middleware already initializes AuditContext with HTTP-level data
        // Nothing to do here unless we need to track method entry time
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnSuccessAsync(MethodContext context)
    {
        var auditContext = AuditContext.Current;
        if (auditContext == null)
        {
            // No HTTP-level audit context, skip method-level enrichment
            return ValueTask.CompletedTask;
        }

        try
        {
            // Enrich with method-level data
            auditContext.IsIntercepted = true;
            auditContext.ServiceName = context.Method.DeclaringType?.Name;
            auditContext.MethodName = context.Method.Name;

            if (_includeParameters && context.Arguments.Length > 0)
            {
                try
                {
                    auditContext.Parameters = context.Arguments.Length == 1
                        ? JsonSerializer.Serialize(context.Arguments[0])
                        : JsonSerializer.Serialize(context.Arguments);
                }
                catch
                {
                    auditContext.Parameters = "[无法序列化参数]";
                }
            }

            if (_includeResult && context.ReturnValue != null)
            {
                try
                {
                    auditContext.ReturnValue = JsonSerializer.Serialize(context.ReturnValue);
                }
                catch
                {
                    auditContext.ReturnValue = "[无法序列化返回值]";
                }
            }

            // If no exception occurred, mark as success
            if (!auditContext.IsException)
            {
                auditContext.HttpStatusCode = 200;
            }
        }
        catch (Exception ex)
        {
            var logger = context.GetService<ILogger<AuditedMoAttribute>>();
            logger?.LogWarning(ex, "审计拦截器 enrichment 失败");
        }

        return ValueTask.CompletedTask;
    }

    public override ValueTask OnExceptionAsync(MethodContext context)
    {
        var auditContext = AuditContext.Current;
        if (auditContext == null)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            auditContext.IsIntercepted = true;
            auditContext.IsException = true;
            auditContext.ServiceName = context.Method.DeclaringType?.Name;
            auditContext.MethodName = context.Method.Name;
            auditContext.ExceptionMessage = context.Exception?.Message;
            auditContext.ExceptionStackTrace = context.Exception?.StackTrace;
            auditContext.HttpStatusCode = 500;

            // Parameters may still be useful even on exception
            if (_includeParameters && context.Arguments.Length > 0)
            {
                try
                {
                    auditContext.Parameters = context.Arguments.Length == 1
                        ? JsonSerializer.Serialize(context.Arguments[0])
                        : JsonSerializer.Serialize(context.Arguments);
                }
                catch
                {
                    // Ignore serialization errors on exception path
                }
            }
        }
        catch (Exception ex)
        {
            var logger = context.GetService<ILogger<AuditedMoAttribute>>();
            logger?.LogWarning(ex, "审计拦截器异常处理失败");
        }

        return ValueTask.CompletedTask;
    }
}
