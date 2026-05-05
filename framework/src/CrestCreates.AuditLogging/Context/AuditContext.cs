using System;
using System.Collections.Generic;
using System.Threading;
using CrestCreates.Domain.AuditLog;

namespace CrestCreates.AuditLogging.Context
{
    /// <summary>
    /// 请求级审计上下文 - 通过AsyncLocal在中间件和拦截器间共享
    /// </summary>
    public sealed class AuditContext
    {
        private static readonly AsyncLocal<AuditContext?> _current = new();

        /// <summary>
        /// 获取当前请求的审计上下文
        /// </summary>
        public static AuditContext? Current => _current.Value;

        /// <summary>
        /// 设置当前审计上下文（中间件使用）
        /// </summary>
        internal static void SetCurrent(AuditContext context) => _current.Value = context;

        /// <summary>
        /// 清除当前审计上下文（写入完成后使用）
        /// </summary>
        internal static void ClearCurrent() => _current.Value = null;

        /// <summary>
        /// 设置当前审计上下文（测试使用）
        /// </summary>
        public static void SetCurrentForTesting(AuditContext context) => _current.Value = context;

        /// <summary>
        /// 清除当前审计上下文（测试使用）
        /// </summary>
        public static void ClearCurrentForTesting() => _current.Value = null;

        /// <summary>
        /// 跟踪ID
        /// </summary>
        public string? TraceId { get; set; }

        /// <summary>
        /// HTTP方法
        /// </summary>
        public string? HttpMethod { get; set; }

        /// <summary>
        /// 请求URL
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// 客户端IP
        /// </summary>
        public string? ClientIpAddress { get; set; }

        /// <summary>
        /// User-Agent
        /// </summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// 请求体（HTTP级别）
        /// </summary>
        public string? RequestBody { get; set; }

        /// <summary>
        /// 响应体（HTTP级别）
        /// </summary>
        public string? ResponseBody { get; set; }

        /// <summary>
        /// HTTP响应状态码
        /// </summary>
        public int HttpStatusCode { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// 租户ID
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        /// 服务名称（类名）- 拦截器填充
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// 方法名称 - 拦截器填充
        /// </summary>
        public string? MethodName { get; set; }

        /// <summary>
        /// 方法参数（JSON）- 拦截器填充
        /// </summary>
        public string? Parameters { get; set; }

        /// <summary>
        /// 返回值（JSON）- 拦截器填充
        /// </summary>
        public string? ReturnValue { get; set; }

        /// <summary>
        /// 异常消息 - 拦截器填充
        /// </summary>
        public string? ExceptionMessage { get; set; }

        /// <summary>
        /// 异常堆栈 - 拦截器填充
        /// </summary>
        public string? ExceptionStackTrace { get; set; }

        /// <summary>
        /// 是否被拦截器处理过
        /// </summary>
        public bool IsIntercepted { get; set; }

        /// <summary>
        /// 是否异常流程
        /// </summary>
        public bool IsException { get; set; }

        /// <summary>
        /// 额外扩展数据
        /// </summary>
        public Dictionary<string, object> ExtraProperties { get; set; } = new();

        /// <summary>
        /// 执行时间点（用于落库）
        /// </summary>
        public DateTime ExecutionTime { get; set; }

        /// <summary>
        /// 将上下文转换为AuditLog实体
        /// </summary>
        public AuditLog ToAuditLog()
        {
            var status = IsException ? AuditLogStatus.Failure : AuditLogStatus.Success;
            var duration = (long)(DateTime.UtcNow - StartTime).TotalMilliseconds;

            return new AuditLog(Guid.NewGuid())
            {
                Duration = duration,
                ExecutionTime = ExecutionTime,
                TraceId = TraceId,
                UserId = UserId,
                UserName = UserName,
                TenantId = TenantId,
                ClientIpAddress = ClientIpAddress,
                HttpMethod = HttpMethod,
                Url = Url,
                ServiceName = ServiceName,
                MethodName = MethodName,
                Parameters = Parameters,
                ReturnValue = ReturnValue,
                ExceptionMessage = ExceptionMessage,
                ExceptionStackTrace = ExceptionStackTrace,
                Status = (int)status,
                CreationTime = DateTime.UtcNow,
                ExtraProperties = ExtraProperties
            };
        }
    }
}
