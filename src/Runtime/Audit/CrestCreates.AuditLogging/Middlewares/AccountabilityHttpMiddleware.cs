using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace CrestCreates.AuditLogging.Middlewares;

/// <summary>
/// Emits one safe post-fact HTTP claim without request/response body or header capture.
/// </summary>
public sealed class AccountabilityHttpMiddleware : IMiddleware
{
    private readonly IAuditRecorder _recorder;
    private readonly IAuditIdentityGenerator _identity;
    private readonly ICurrentTenant _tenant;
    private readonly ILogger<AccountabilityHttpMiddleware> _logger;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly IAuditedMethodAccountabilityRuntime _methodRuntime;
    private readonly TimeProvider _timeProvider;

    public AccountabilityHttpMiddleware(
        IAuditRecorder recorder,
        IAuditIdentityGenerator identity,
        ICurrentTenant tenant,
        ILogger<AccountabilityHttpMiddleware> logger,
        IAuditOperationContextAccessor contexts,
        IAuditedMethodAccountabilityRuntime methodRuntime,
        TimeProvider? timeProvider = null)
    {
        _recorder = recorder;
        _identity = identity;
        _tenant = tenant;
        _logger = logger;
        _contexts = contexts;
        _methodRuntime = methodRuntime;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        using var methodRuntimeScope = AuditedMethodAccountabilityRuntimeContext.Push(_methodRuntime);
        var started = Stopwatch.GetTimestamp();
        var auditId = _identity.CreateAuditId();
        var operationId = _identity.CreateOperationId();
        var correlation = _identity.CreateOperationId();
        var actor = ResolveActor(context.User);
        var scope = _contexts.Push(new AuditOperationContext
        {
            CorrelationId = correlation,
            OperationId = operationId,
            EnclosingAuditId = auditId,
            Actor = actor,
            TenantId = _tenant.Id,
            InvocationSource = "http",
            InitiatingOperationId = operationId,
            InitiatingAuditId = auditId
        });
        Exception? failure = null;
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            var occurredAt = _timeProvider.GetUtcNow();
            var duration = Stopwatch.GetElapsedTime(started);
            var requestCancelled = failure is OperationCanceledException || context.RequestAborted.IsCancellationRequested;
            var statusCode = requestCancelled ? 499 : failure is null ? context.Response.StatusCode : 500;
            var outcome = requestCancelled
                ? new AuditOutcome { Status = "cancelled", Code = "HTTP_CANCELLED" }
                : new AuditOutcome
                {
                    Status = statusCode is >= 200 and < 400 ? "succeeded" : "failed",
                    Code = failure is not null
                        ? "UNHANDLED_EXCEPTION"
                        : statusCode is >= 500
                            ? $"HTTP_{statusCode}"
                            : statusCode is >= 400
                                ? $"HTTP_{statusCode}"
                                : null
                };

            var method = context.Request.Method.ToUpperInvariant();
            var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            var endpointIdentity = string.IsNullOrWhiteSpace(routeTemplate)
                ? $"{method} <unmatched>"
                : $"{method} {NormalizeRouteTemplate(routeTemplate)}";
            var envelope = new AuditEnvelope
            {
                AuditId = auditId,
                OccurredAt = occurredAt,
                TenantId = _tenant.Id,
                CorrelationId = correlation,
                CausationId = null,
                Actor = actor,
                Action = new AuditAction { Kind = "http.request", Name = endpointIdentity },
                Target = new AuditTarget { Kind = "http.endpoint", Id = endpointIdentity },
                Outcome = outcome,
                Runtime = new AuditRuntimeContext
                {
                    InvocationSource = "http",
                    ExecutionId = operationId,
                    RequestId = context.TraceIdentifier,
                    TraceId = Activity.Current?.TraceId.ToString(),
                    SpanId = Activity.Current?.SpanId.ToString(),
                    Duration = duration,
                    References = []
                },
                Descriptors = AuditDescriptorContext.Empty,
                Evidence = [],
                Tags = AuditTagMap.Empty
            };

            try
            {
                await _recorder.RecordAsync(envelope, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Accountability HTTP post-fact recording failed for {Endpoint}", endpointIdentity);
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }

    private static AuditActor ResolveActor(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return new AuditActor { Kind = "anonymous", Id = "anonymous" };
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(id)
            ? new AuditActor { Kind = "unknown", Id = "unknown" }
            : new AuditActor { Kind = "user", Id = id, DisplayName = principal.Identity.Name };
    }

    private static string NormalizeRouteTemplate(string routeTemplate)
    {
        var normalized = routeTemplate.Trim();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }
}

public static class AccountabilityHttpMiddlewareExtensions
{
    public static IApplicationBuilder UseAccountabilityHttpAudit(this IApplicationBuilder builder)
        => builder.UseMiddleware<AccountabilityHttpMiddleware>();
}
