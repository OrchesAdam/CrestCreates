using CrestCreates.Authorization.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Web.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex, _logger);
            }
        }

        private static async Task HandleExceptionAsync(
            HttpContext context,
            Exception exception,
            ILogger<ExceptionHandlingMiddleware> logger)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var errorResponse = new ErrorResponse
            {
                Code = context.Response.StatusCode,
                Message = "服务器内部错误",
                TraceId = context.TraceIdentifier
            };

            switch (exception)
            {
                case UnauthorizedAccessException:
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    errorResponse.Code = (int)HttpStatusCode.Unauthorized;
                    errorResponse.Message = "未授权访问";
                    logger.LogWarning(exception, "Unauthorized access for request {TraceId}", context.TraceIdentifier);
                    break;
                case CrestPermissionException permissionException:
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    errorResponse.Code = (int)HttpStatusCode.Forbidden;
                    errorResponse.Message = "没有权限执行当前操作";
                    errorResponse.Details = permissionException.Message;
                    logger.LogWarning(permissionException, "Permission denied for request {TraceId}", context.TraceIdentifier);
                    break;
                case ArgumentException argEx:
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Code = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "参数错误";
                    errorResponse.Details = argEx.Message;
                    logger.LogWarning(argEx, "Bad request caused by argument validation for {TraceId}", context.TraceIdentifier);
                    break;
                case InvalidOperationException invalidOpEx:
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Code = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "操作无效";
                    errorResponse.Details = invalidOpEx.Message;
                    logger.LogWarning(invalidOpEx, "Invalid operation for request {TraceId}", context.TraceIdentifier);
                    break;
                case System.ComponentModel.DataAnnotations.ValidationException validationEx:
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Code = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "数据验证失败";
                    errorResponse.Details = validationEx.Message;
                    logger.LogWarning(validationEx, "Validation failure for request {TraceId}", context.TraceIdentifier);
                    break;
                default:
                    logger.LogError(exception, "Unhandled exception for request {TraceId}", context.TraceIdentifier);
                    break;
            }

            var jsonResponse = JsonSerializer.Serialize(errorResponse);
            await context.Response.WriteAsync(jsonResponse);
        }
    }

    public class ErrorResponse
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public string? TraceId { get; set; }
    }

    public static class ExceptionHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ExceptionHandlingMiddleware>();
        }
    }
}
