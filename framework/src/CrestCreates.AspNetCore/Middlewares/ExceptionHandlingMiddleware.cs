using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.AspNetCore.Localization;
using CrestCreates.AspNetCore.Serialization;
using CrestCreates.Localization.Abstractions;
using CrestCreates.Localization.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.AspNetCore.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICrestExceptionConverter _converter;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly CrestErrorResponseJsonContext _jsonContext;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ICrestExceptionConverter converter,
        ILogger<ExceptionHandlingMiddleware> logger,
        CrestErrorResponseJsonContext jsonContext,
        IOptions<JsonOptions>? jsonOptions = null)
    {
        _next = next;
        _converter = converter;
        _logger = logger;
        _jsonContext = jsonContext;
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

        var typeInfo = CrestErrorResponseJsonContext.Default.CrestErrorResponse;
        await JsonSerializer.SerializeAsync(context.Response.Body, response, typeInfo, context.RequestAborted);
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
        services.TryAddSingleton<ILocalizationService>(sp =>
        {
            var factory = sp.GetRequiredService<IStringLocalizerFactory>();
            var service = new LocalizationService(factory);
            var jsonResources = LoadExceptionLocalizationResources();
            service.RegisterResource(new LocalizationResource
            {
                Name = "ExceptionErrorCodes",
                Contributor = new DictionaryResourceContributor("ExceptionErrorCodes", jsonResources),
                Priority = 0
            });
            return service;
        });
        services.TryAddSingleton<ICrestExceptionConverter, DefaultCrestExceptionConverter>();
        services.TryAddSingleton<CrestErrorResponseJsonContext>();
        return services;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadExceptionLocalizationResources()
    {
        var assembly = typeof(CrestCreates.Domain.Shared.Exceptions.CrestException).Assembly;
        var cultures = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".json") || !resourceName.Contains("Localization"))
                continue;

            var culture = ExtractCultureFromResourceName(resourceName);
            if (culture is null)
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (entries is not null)
                cultures[culture] = entries;
        }

        return cultures;
    }

    private static string? ExtractCultureFromResourceName(string resourceName)
    {
        var fileName = resourceName.Split('.').Reverse().ElementAtOrDefault(1);
        if (fileName is null)
            return null;

        return fileName switch
        {
            "en" => "en",
            "zh-CN" => "zh-CN",
            _ => null
        };
    }

    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
