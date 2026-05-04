using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Localization.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.AspNetCore.Errors;

public class DefaultCrestExceptionConverter : ICrestExceptionConverter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CrestExceptionLocalizationResources _resources;
    private readonly ILogger<DefaultCrestExceptionConverter> _logger;

    public DefaultCrestExceptionConverter(
        IServiceProvider serviceProvider,
        CrestExceptionLocalizationResources resources,
        ILogger<DefaultCrestExceptionConverter> logger)
    {
        _serviceProvider = serviceProvider;
        _resources = resources;
        _logger = logger;
    }

    public CrestExceptionConversionResult Convert(HttpContext context, Exception exception)
    {
        var response = exception switch
        {
            CrestPermissionException permissionException => Create(context, "Crest.Auth.Forbidden", 403, "没有权限执行当前操作。", permissionException.PermissionName),
            CrestException crestException => FromCrestException(context, crestException),
            UnauthorizedAccessException => MapUnauthorized(context),
            KeyNotFoundException keyNotFoundException => Create(context, "Crest.Entity.NotFound", 404, "资源不存在。", keyNotFoundException.Message),
            ValidationException validationException => Create(context, "Crest.Validation.Failed", 400, "数据验证失败。", validationException.Message),
            ArgumentException => Create(context, "Crest.Request.InvalidArgument", 400, "请求参数错误。"),
            InvalidOperationException => Create(context, "Crest.Operation.Invalid", 400, "当前操作无效。"),
            _ => Create(context, "Crest.InternalError", 500, "服务器内部错误。")
        };

        var logLevel = response.StatusCode >= 500 ? LogLevel.Error : LogLevel.Warning;
        return new CrestExceptionConversionResult(response, logLevel);
    }

    private CrestErrorResponse MapUnauthorized(HttpContext context)
    {
        var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
        return isAuthenticated
            ? Create(context, "Crest.Auth.Forbidden", 403, "没有权限执行当前操作。")
            : Create(context, "Crest.Auth.Unauthorized", 401, "当前请求未认证。");
    }

    private CrestErrorResponse FromCrestException(HttpContext context, CrestException exception)
    {
        return Create(context, exception.ErrorCode, exception.HttpStatusCode, exception.Message, exception.Details);
    }

    private CrestErrorResponse Create(
        HttpContext context,
        string errorCode,
        int statusCode,
        string fallbackMessage,
        string? details = null)
    {
        return new CrestErrorResponse
        {
            Code = errorCode,
            Message = Localize(errorCode, fallbackMessage),
            Details = statusCode >= 500 ? null : details,
            TraceId = context.TraceIdentifier,
            StatusCode = statusCode
        };
    }

    private string Localize(string errorCode, string fallbackMessage)
    {
        // Check exception-specific resources first (loaded from embedded JSON, avoids DI conflict with LocalizationModule)
        var currentCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
        if (_resources.Cultures.TryGetValue(currentCulture, out var entries)
            && entries.TryGetValue(errorCode, out var resourceValue))
        {
            return resourceValue;
        }

        // Fall back to ILocalizationService (registered by LocalizationModule)
        var localizationService = _serviceProvider.GetService<ILocalizationService>();
        if (localizationService is not null)
        {
            try
            {
                var localized = localizationService.GetString(errorCode);
                if (!string.IsNullOrWhiteSpace(localized) && localized != errorCode)
                    return localized;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to localize error code {ErrorCode}", errorCode);
            }
        }

        return fallbackMessage;
    }
}
