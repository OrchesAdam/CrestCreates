using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.AuditLogging.Abstractions.MethodAccountability;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.AuditLogging.Middlewares;

/// <summary>
/// Enriches the HTTP request with authenticated responsibility context for downstream operations.
/// </summary>
public sealed class AccountabilityHttpOperationScopeMiddleware : IMiddleware
{
    private readonly ICurrentTenant _tenant;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly IAuditedMethodAccountabilityRuntime _methodRuntime;

    public AccountabilityHttpOperationScopeMiddleware(
        ICurrentTenant tenant,
        IAuditOperationContextAccessor contexts,
        IAuditedMethodAccountabilityRuntime methodRuntime)
    {
        _tenant = tenant;
        _contexts = contexts;
        _methodRuntime = methodRuntime;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var state = context.Features.Get<AccountabilityHttpRequestState>()
            ?? throw new InvalidOperationException(
                "The Accountability HTTP operation scope requires the terminal observer to run first.");

        var actor = ResolveActor(context.User);
        var tenantId = _tenant.Id;
        state.Actor = actor;
        state.TenantId = tenantId;

        using var methodRuntimeScope = AuditedMethodAccountabilityRuntimeContext.Push(_methodRuntime);
        using var operationScope = _contexts.Push(new AuditOperationContext
        {
            CorrelationId = state.CorrelationId,
            OperationId = state.OperationId,
            EnclosingAuditId = state.AuditId,
            Actor = actor,
            TenantId = tenantId,
            InvocationSource = "http",
            InitiatingOperationId = state.OperationId,
            InitiatingAuditId = state.AuditId
        });
        await next(context).ConfigureAwait(false);
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
}

public static class AccountabilityHttpOperationScopeMiddlewareExtensions
{
    public static IApplicationBuilder UseAccountabilityHttpOperationScope(this IApplicationBuilder builder)
        => builder.UseMiddleware<AccountabilityHttpOperationScopeMiddleware>();
}
