using System;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DynamicApi;

internal static class CapabilityEndpointMapper
{
    public static void MapEndpoint(
        IEndpointRouteBuilder endpoints,
        CapabilityEndpointDescriptor descriptor,
        CapabilityDescriptor capability,
        CapabilityEndpointBindingContract binding,
        Func<EndpointExecutionContext, IServiceProvider, object>? resultMapper = null)
    {
        var httpMethod = descriptor.HttpMethod.ToString().ToUpperInvariant();

        var routeHandler = endpoints.MapMethods(
            descriptor.RoutePattern,
            new[] { httpMethod },
            async (HttpContext context) =>
            {
                object? input;
                try
                {
                    input = await binding.BindInputAsync(context, context.RequestAborted);
                }
                catch (Exception ex) when (ex is BadHttpRequestException
                    or JsonException or FormatException or ArgumentException)
                {
                    // Binding/parse failures are local 400s: no business code and no write ran.
                    var presenter = context.RequestServices
                        .GetService<ICapabilityEndpointFailurePresenter>();
                    return presenter is not null
                        ? presenter.PresentBindingFailure(ex, context)
                        : Results.BadRequest();
                }
                catch (OperationCanceledException ex)
                {
                    // Binding-stage cancellation follows the same frozen identity rules as the
                    // pipeline: only a canceled caller token suppresses the response; every other
                    // source is an internal failure rendered as a sanitized 500.
                    if (ex.CancellationToken == context.RequestAborted
                        && context.RequestAborted.IsCancellationRequested)
                    {
                        return Results.StatusCode(499);
                    }

                    var facts = new CapabilityFailureFacts(
                        CapabilityFailureKind.NonCaller,
                        "Crest.InternalError",
                        "服务器内部错误。");
                    var cancelled = CapabilityExecutionResult.Failure(
                        "PIPELINE_ERROR", ex.Message, TimeSpan.Zero, failureFacts: facts);
                    var presenter = context.RequestServices
                        .GetService<ICapabilityEndpointFailurePresenter>();
                    return presenter is not null
                        ? presenter.Present(cancelled, context)
                        : Results.StatusCode(StatusCodes.Status500InternalServerError);
                }

                var dispatcher = context.RequestServices
                    .GetRequiredService<ICapabilityDispatcher>();
                var result = await dispatcher.DispatchAsync(
                    capability, InvocationSource.Http, input,
                    ctx =>
                    {
                        ctx.IdempotencyKey = ResolveIdempotencyKey(context);
                        if (input is not null)
                        {
                            var typeInfo = CapabilityEndpointJsonTypeInfoResolver.Resolve(context, input.GetType())
                                ?? throw new InvalidOperationException(
                                    $"No JsonTypeInfo is registered for capability input '{input.GetType()}'.");
                            ctx.InputJson = JsonSerializer.SerializeToElement(input, typeInfo);
                        }
                        ctx.Items["HttpTraceIdentifier"] = context.TraceIdentifier;
                        ctx.Items["CapabilityEndpointId"] = descriptor.Id;
                    },
                    context.RequestAborted);

                return MapResult(result, descriptor.OutputMapping, resultMapper, context);
            });

        // Apply endpoint metadata
        routeHandler.WithDisplayName($"{descriptor.Capability.Id} → {descriptor.RoutePattern}");

        if (descriptor.Projection.Tags is { Count: > 0 } tags)
            routeHandler.WithTags(tags.ToArray());
        if (descriptor.Projection.OperationId is not null)
            routeHandler.WithName(descriptor.Projection.OperationId);

        // 8a only applies Tags and OperationId to Minimal API metadata.
        // GroupName/Summary/Description/Deprecated/Visibility are stored in
        // Projection metadata for future OpenAPI integration.

        // Authorization
        if (descriptor.AuthorizationMode == CapabilityEndpointAuthorizationMode.RequireAuthenticated)
            routeHandler.RequireAuthorization();
        else if (descriptor.AuthorizationMode == CapabilityEndpointAuthorizationMode.AllowAnonymous)
            routeHandler.AllowAnonymous();
    }

    private static string ResolveIdempotencyKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var key)
            && !string.IsNullOrWhiteSpace(key))
            return key!;
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Maps a <see cref="CapabilityExecutionResult"/> to an ASP.NET Core <see cref="IResult"/>.
    /// If a custom result mapper is registered for the endpoint, it takes precedence;
    /// otherwise the default <see cref="CapabilityEndpointResultMapper.Map"/> is used.
    /// </summary>
    private static IResult MapResult(
        CapabilityExecutionResult result,
        CapabilityEndpointOutputMapping outputMapping,
        Func<EndpointExecutionContext, IServiceProvider, object>? resultMapper,
        HttpContext httpContext)
    {
        // Custom result contracts only govern the *success* response envelope.
        // Pipeline failures (authorization, validation, rate-limit, handler-not-found, etc.)
        // must always be mapped by the unified CapabilityEndpointResultMapper so that
        // compatibility projections never swallow a failure as a 200 OK.
        if (!result.IsSuccess)
        {
            var presenter = httpContext.RequestServices
                .GetService<ICapabilityEndpointFailurePresenter>();
            return presenter is not null
                ? presenter.Present(result, httpContext)
                : CapabilityEndpointResultMapper.Map(result, outputMapping, httpContext);
        }

        if (resultMapper is not null)
        {
            var ctx = new EndpointExecutionContext
            {
                Output = result.Output,
                Succeeded = true,
                ErrorCode = null,
                ErrorMessage = null
            };

            var mapped = resultMapper(ctx, httpContext.RequestServices);
            if (mapped is CapabilityEndpointNotFoundResult)
            {
                var presenter = httpContext.RequestServices
                    .GetService<ICapabilityEndpointFailurePresenter>();
                return presenter is not null
                    ? presenter.PresentNotFound(httpContext)
                    : Results.NotFound();
            }

            // The mapper returns object to avoid AspNetCore dependency in
            // Abstractions, but the concrete implementation always returns IResult.
            return (IResult)mapped;
        }

        return CapabilityEndpointResultMapper.Map(result, outputMapping, httpContext);
    }
}
