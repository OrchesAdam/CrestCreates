using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;

namespace CrestCreates.Web.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ExceptionHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var errorResponse = new ErrorResponse
            {
                Code = context.Response.StatusCode,
                Message = "服务器内部错误",
                Details = exception.Message
            };

            // 根据异常类型设置不同的状态码和消息
            switch (exception)
            {
                case UnauthorizedAccessException:
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    errorResponse.Code = (int)HttpStatusCode.Unauthorized;
                    errorResponse.Message = "未授权访问";
                    break;
                case ArgumentException argEx:
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Code = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "参数错误";
                    errorResponse.Details = argEx.Message;
                    break;
                case InvalidOperationException invalidOpEx:
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Code = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "操作无效";
                    errorResponse.Details = invalidOpEx.Message;
                    break;
                case System.ComponentModel.DataAnnotations.ValidationException validationEx:
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Code = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "数据验证失败";
                    errorResponse.Details = validationEx.Message;
                    break;
                default:
                    // 记录未处理的异常
                    // 这里可以添加日志记录
                    break;
            }

            var jsonResponse = JsonConvert.SerializeObject(errorResponse);
            await context.Response.WriteAsync(jsonResponse);
        }
    }

    public class ErrorResponse
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
    }

    public static class ExceptionHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ExceptionHandlingMiddleware>();
        }
    }
}