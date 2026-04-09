using CrestCreates.AuditLogging.Entities;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AuditLogging.Services;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Extensions;

namespace CrestCreates.AuditLogging.Middlewares
{
    public class AuditLoggingMiddleware : IMiddleware
    {
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentTenant _currentTenant;
        private readonly AuditLoggingOptions _options;
        private readonly ILogger<AuditLoggingMiddleware> _logger;

        public AuditLoggingMiddleware(
            IAuditLogService auditLogService,
            ICurrentTenant currentTenant,
            IOptions<AuditLoggingOptions> options,
            ILogger<AuditLoggingMiddleware> logger)
        {
            _auditLogService = auditLogService;
            _currentTenant = currentTenant;
            _options = options.Value;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (!_options.IsEnabled)
            {
                await next(context);
                return;
            }

            var requestPath = context.Request.Path.ToString();
            var shouldAuditRequest = ShouldAuditRequest(context.Request.Method, requestPath);
            var shouldPersistAuditLog = shouldAuditRequest;
            Exception? capturedException = null;
            var auditLog = new AuditLog(Guid.NewGuid())
            {
                CreationTime = DateTime.UtcNow,
                ClientIpAddress = context.Connection.RemoteIpAddress?.ToString(),
                ClientName = context.Request.Headers["User-Agent"].ToString(),
                Path = requestPath,
                HttpMethod = context.Request.Method,
                TenantId = _currentTenant.Id,
                Action = $"{context.Request.Method} {requestPath}",
                Description = context.Request.GetDisplayUrl(),
                ExtraProperties = new Dictionary<string, object>
                {
                    ["TraceIdentifier"] = context.TraceIdentifier
                }
            };

            if (context.User?.Identity?.IsAuthenticated == true)
            {
                auditLog.UserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                auditLog.UserName = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            }

            if (shouldAuditRequest && _options.IncludeRequestBody)
            {
                auditLog.Request = await TryReadRequestBodyAsync(context.Request);
            }

            var originalResponseStream = context.Response.Body;
            using (var responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;

                var startTime = DateTime.UtcNow;
                try
                {
                    await next(context);
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                    auditLog.Exception = ex.ToString();
                }
                finally
                {
                    context.Response.Body = originalResponseStream;
                    var endTime = DateTime.UtcNow;
                    auditLog.ExecutionTime = (long)(endTime - startTime).TotalMilliseconds;
                    auditLog.StatusCode = context.Response.StatusCode;

                    responseBody.Seek(0, SeekOrigin.Begin);
                    if (_options.IncludeResponseBody && shouldAuditRequest)
                    {
                        auditLog.Response = await ReadAndTrimResponseBodyAsync(responseBody);
                    }
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalResponseStream);

                    shouldPersistAuditLog = shouldAuditRequest ||
                        (capturedException != null && _options.AlwaysLogOnException);
                }
            }

            if (!shouldPersistAuditLog)
            {
                return;
            }

            try
            {
                await _auditLogService.CreateAsync(auditLog);
            }
            catch (Exception saveException)
            {
                _logger.LogWarning(saveException, "Failed to persist audit log for {RequestPath}", requestPath);
                if (!_options.HideErrors)
                {
                    throw;
                }
            }

            if (capturedException is not null)
            {
                throw capturedException;
            }
        }

        private bool ShouldAuditRequest(string method, string path)
        {
            if (!_options.IsEnabledForGetRequests &&
                HttpMethods.IsGet(method))
            {
                return false;
            }

            return !_options.IgnoredUrls.Any(ignored =>
                !string.IsNullOrWhiteSpace(ignored) &&
                path.StartsWith(ignored, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<string?> TryReadRequestBodyAsync(HttpRequest request)
        {
            if (request.ContentLength is null or 0)
            {
                return null;
            }

            request.EnableBuffering();
            request.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            request.Body.Seek(0, SeekOrigin.Begin);
            return TrimAndSanitize(raw, _options.MaxRequestBodyLength);
        }

        private async Task<string?> ReadAndTrimResponseBodyAsync(Stream responseBody)
        {
            using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            return TrimAndSanitize(raw, _options.MaxResponseBodyLength);
        }

        private string? TrimAndSanitize(string? raw, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            var sanitized = SanitizeJson(raw);
            if (sanitized.Length <= maxLength)
            {
                return sanitized;
            }

            return sanitized[..maxLength] + "...";
        }

        private string SanitizeJson(string raw)
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                var sanitized = SanitizeElement(document.RootElement);
                return JsonSerializer.Serialize(sanitized);
            }
            catch (JsonException)
            {
                return raw;
            }
        }

        private object? SanitizeElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => IsSensitive(property.Name)
                            ? "***"
                            : SanitizeElement(property.Value)),
                JsonValueKind.Array => element.EnumerateArray()
                    .Select(SanitizeElement)
                    .ToList(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var value) ? value : element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        private bool IsSensitive(string propertyName)
        {
            return _options.SensitivePropertyNames.Any(sensitive =>
                string.Equals(sensitive, propertyName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class AuditLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuditLoggingMiddleware>();
        }
    }
}
