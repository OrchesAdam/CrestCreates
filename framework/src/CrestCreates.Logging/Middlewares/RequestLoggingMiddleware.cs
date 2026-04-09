using System.Diagnostics;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Logging.Middlewares;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUser = context.RequestServices.GetService<ICurrentUser>();
        var currentTenant = context.RequestServices.GetService<ICurrentTenant>();
        var activity = Activity.Current;

        var scope = new Dictionary<string, object?>
        {
            ["TraceId"] = activity?.TraceId.ToString() ?? context.TraceIdentifier,
            ["SpanId"] = activity?.SpanId.ToString(),
            ["RequestPath"] = context.Request.Path.ToString(),
            ["RequestMethod"] = context.Request.Method,
            ["ClientIpAddress"] = context.Connection.RemoteIpAddress?.ToString(),
            ["UserId"] = currentUser?.Id,
            ["UserName"] = currentUser?.UserName,
            ["TenantId"] = currentTenant?.Id ?? currentUser?.TenantId
        };

        using (_logger.BeginScope(scope))
        {
            await _next(context);
            stopwatch.Stop();

            _logger.LogInformation(
                "Handled HTTP {RequestMethod} {RequestPath} => {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path.ToString(),
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
