using System.Runtime.ExceptionServices;
using System.Text.Json;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.Localization.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.AspNetCore.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICrestExceptionConverter _converter;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ICrestExceptionConverter converter,
        ILogger<ExceptionHandlingMiddleware> logger,
        IOptions<JsonOptions>? jsonOptions = null)
    {
        _next = next;
        _converter = converter;
        _logger = logger;
        _jsonSerializerOptions = jsonOptions?.Value.SerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var conversion = _converter.Convert(context, exception);
        var response = conversion.Response;

        LogException(exception, response, conversion.LogLevel);

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write error response because response has already started. TraceId={TraceId}, ErrorCode={ErrorCode}",
                response.TraceId,
                response.Code);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = response.StatusCode;

        await JsonSerializer.SerializeAsync(context.Response.Body, response, _jsonSerializerOptions, context.RequestAborted);
    }

    private void LogException(Exception exception, CrestErrorResponse response, LogLevel logLevel)
    {
        _logger.Log(
            logLevel,
            exception,
            "Request failed with {StatusCode} {ErrorCode}. TraceId={TraceId}",
            response.StatusCode,
            response.Code,
            response.TraceId);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IServiceCollection AddCrestExceptionHandling(this IServiceCollection services)
    {
        services.AddLocalization();
        services.TryAddSingleton<ILocalizationService, LocalizationService>();
        services.TryAddSingleton<ICrestExceptionConverter, DefaultCrestExceptionConverter>();
        return services;
    }

    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
