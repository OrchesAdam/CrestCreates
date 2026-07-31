using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace CrestCreates.AuditLogging.Middlewares;

/// <summary>
/// Observes the terminal HTTP response outside global exception handling and emits one safe post-fact claim.
/// </summary>
public sealed class AccountabilityHttpTerminalObserverMiddleware : IMiddleware
{
    private readonly IAuditRecorder _recorder;
    private readonly IAuditIdentityGenerator _identity;
    private readonly ILogger<AccountabilityHttpTerminalObserverMiddleware> _logger;
    private readonly TimeProvider _timeProvider;

    public AccountabilityHttpTerminalObserverMiddleware(
        IAuditRecorder recorder,
        IAuditIdentityGenerator identity,
        ILogger<AccountabilityHttpTerminalObserverMiddleware> logger,
        TimeProvider? timeProvider = null)
    {
        _recorder = recorder;
        _identity = identity;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var state = new AccountabilityHttpRequestState
        {
            AuditId = _identity.CreateAuditId(),
            OperationId = _identity.CreateOperationId(),
            CorrelationId = _identity.CreateOperationId(),
            StartedTimestamp = Stopwatch.GetTimestamp()
        };
        context.Features.Set(state);

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
            var duration = Stopwatch.GetElapsedTime(state.StartedTimestamp);
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
                AuditId = state.AuditId,
                OccurredAt = occurredAt,
                TenantId = state.TenantId,
                CorrelationId = state.CorrelationId,
                CausationId = null,
                Actor = state.Actor,
                Action = new AuditAction { Kind = "http.request", Name = endpointIdentity },
                Target = new AuditTarget { Kind = "http.endpoint", Id = endpointIdentity },
                Outcome = outcome,
                Runtime = new AuditRuntimeContext
                {
                    InvocationSource = "http",
                    ExecutionId = state.OperationId,
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
        }
    }

    private static string NormalizeRouteTemplate(string routeTemplate)
    {
        var normalized = routeTemplate.Trim();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }
}

internal sealed class AccountabilityHttpRequestState
{
    public required string AuditId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required long StartedTimestamp { get; init; }
    public AuditActor Actor { get; set; } = new() { Kind = "unknown", Id = "unknown" };
    public string? TenantId { get; set; }
}

public static class AccountabilityHttpTerminalObserverMiddlewareExtensions
{
    public static IApplicationBuilder UseAccountabilityHttpTerminalObserver(this IApplicationBuilder builder)
        => builder.UseMiddleware<AccountabilityHttpTerminalObserverMiddleware>();
}
