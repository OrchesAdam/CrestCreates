using CrestCreates.AuditLogging.Context;
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
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Extensions;

namespace CrestCreates.AuditLogging.Middlewares
{
    public class AuditLoggingMiddleware : IMiddleware
    {
        private readonly IAuditLogWriter _auditLogWriter;
        private readonly ICurrentTenant _currentTenant;
        private readonly AuditLoggingOptions _options;
        private readonly ILogger<AuditLoggingMiddleware> _logger;

        public AuditLoggingMiddleware(
            IAuditLogWriter auditLogWriter,
            ICurrentTenant currentTenant,
            IOptions<AuditLoggingOptions> options,
            ILogger<AuditLoggingMiddleware> logger)
        {
            _auditLogWriter = auditLogWriter;
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
            if (!ShouldAuditRequest(context.Request.Method, requestPath))
            {
                await next(context);
                return;
            }

            // Capture exception for later rethrow while preserving stack trace
            ExceptionDispatchInfo? exceptionInfo = null;

            // Initialize AuditContext and store in AsyncLocal
            var auditContext = new AuditContext
            {
                TraceId = context.TraceIdentifier,
                HttpMethod = context.Request.Method,
                Url = context.Request.GetDisplayUrl(),
                ClientIpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers["User-Agent"].ToString(),
                StartTime = DateTime.UtcNow,
                ExecutionTime = DateTime.UtcNow,
                TenantId = _currentTenant.Id,
                HttpStatusCode = 200,
                ExtraProperties = new Dictionary<string, object>()
            };

            if (context.User?.Identity?.IsAuthenticated == true)
            {
                auditContext.UserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                auditContext.UserName = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            }

            AuditContext.SetCurrent(auditContext);

            if (_options.IncludeRequestBody)
            {
                auditContext.RequestBody = await TryReadRequestBodyAsync(context.Request);
            }

            var originalResponseStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                // Capture exception with full stack trace preservation
                exceptionInfo = ExceptionDispatchInfo.Capture(ex);

                // Enrich audit context with exception details
                auditContext.IsException = true;
                auditContext.ExceptionMessage = ex.Message;
                auditContext.ExceptionStackTrace = ex.StackTrace;
                auditContext.HttpStatusCode = 500;
            }
            finally
            {
                // Always restore response body first
                context.Response.Body = originalResponseStream;

                // Capture response body if needed
                if (_options.IncludeResponseBody)
                {
                    responseBody.Seek(0, SeekOrigin.Begin);
                    auditContext.ResponseBody = await ReadAndTrimResponseBodyAsync(responseBody);
                }

                // Copy response body to original stream
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalResponseStream);

                // Get actual HTTP status code from response
                auditContext.HttpStatusCode = context.Response.StatusCode;

                // Write the unified audit log
                try
                {
                    await _auditLogWriter.WriteAsync(auditContext);
                }
                catch (Exception writeEx)
                {
                    _logger.LogWarning(writeEx, "Failed to persist audit log for {RequestPath}", requestPath);
                    if (!_options.HideErrors)
                    {
                        throw;
                    }
                }

                AuditContext.ClearCurrent();
            }

            // Rethrow the captured exception AFTER finally cleanup, preserving original stack trace
            exceptionInfo?.Throw();
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
            // Memory protection: cap at MaxRequestBodyLength; actual sanitization happens in IAuditLogRedactor
            return Trim(raw, _options.MaxRequestBodyLength);
        }

        private async Task<string?> ReadAndTrimResponseBodyAsync(Stream responseBody)
        {
            using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            // Memory protection: cap at MaxResponseBodyLength; actual sanitization happens in IAuditLogRedactor
            return Trim(raw, _options.MaxResponseBodyLength);
        }

        private string? Trim(string? raw, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            if (raw.Length <= maxLength)
            {
                return raw;
            }

            return raw[..maxLength] + "...";
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
