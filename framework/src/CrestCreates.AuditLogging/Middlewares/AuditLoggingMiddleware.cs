using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Entities;
using CrestCreates.AuditLogging.Services;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.AuditLogging.Middlewares
{
    public class AuditLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentTenant _currentTenant;

        public AuditLoggingMiddleware(
            RequestDelegate next,
            IAuditLogService auditLogService,
            ICurrentTenant currentTenant)
        {
            _next = next;
            _auditLogService = auditLogService;
            _currentTenant = currentTenant;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var auditLog = new AuditLog(Guid.NewGuid())
            {
                CreationTime = DateTime.UtcNow,
                ClientIpAddress = context.Connection.RemoteIpAddress?.ToString(),
                ClientName = context.Request.Headers["User-Agent"].ToString(),
                Path = context.Request.Path.ToString(),
                HttpMethod = context.Request.Method,
                TenantId = _currentTenant.Id
            };

            // 获取用户信息
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                auditLog.UserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                auditLog.UserName = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            }

            // 记录请求体
            if (context.Request.Method != "GET" && context.Request.ContentLength > 0)
            {
                // 简化处理，直接读取请求体
                using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
                {
                    var requestBody = await reader.ReadToEndAsync();
                    auditLog.Request = requestBody;
                    context.Request.Body.Position = 0;
                }
            }

            // 记录响应体
            var originalResponseStream = context.Response.Body;
            using (var responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;

                var startTime = DateTime.UtcNow;
                try
                {
                    await _next(context);
                }
                catch (Exception ex)
                {
                    auditLog.Exception = ex.ToString();
                    throw;
                }
                finally
                {
                    var endTime = DateTime.UtcNow;
                    auditLog.ExecutionTime = (long)(endTime - startTime).TotalMilliseconds;
                    auditLog.StatusCode = context.Response.StatusCode;

                    // 读取响应体
                    context.Response.Body.Seek(0, SeekOrigin.Begin);
                    var responseBodyText = await new StreamReader(context.Response.Body).ReadToEndAsync();
                    context.Response.Body.Seek(0, SeekOrigin.Begin);

                    // 限制响应体大小，避免日志过大
                    if (responseBodyText.Length > 1024)
                    {
                        auditLog.Response = responseBodyText.Substring(0, 1024) + "...";
                    }
                    else
                    {
                        auditLog.Response = responseBodyText;
                    }

                    // 复制回原始响应流
                    await responseBody.CopyToAsync(originalResponseStream);

                    // 异步记录审计日志，不阻塞请求
                    _ = _auditLogService.CreateAsync(auditLog);
                }
            }
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