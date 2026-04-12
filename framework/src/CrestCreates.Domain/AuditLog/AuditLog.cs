using System;
using System.Collections.Generic;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.AuditLog
{
    /// <summary>
    /// 统一审计日志实体 - 合并请求级、方法级、异常级审计
    /// </summary>
    public class AuditLog : Entity<Guid>
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public AuditLog()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="id">审计日志ID</param>
        public AuditLog(Guid id)
        {
            Id = id;
        }

        /// <summary>
        /// 执行时长（毫秒）
        /// </summary>
        public long Duration { get; set; }

        /// <summary>
        /// 操作人ID
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// 操作人名称
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// 租户ID
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>
        /// 客户端IP地址
        /// </summary>
        public string? ClientIpAddress { get; set; }

        /// <summary>
        /// HTTP方法
        /// </summary>
        public string? HttpMethod { get; set; }

        /// <summary>
        /// 请求URL
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// 服务名称（类名）
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// 方法名称
        /// </summary>
        public string? MethodName { get; set; }

        /// <summary>
        /// 方法参数（JSON格式）
        /// </summary>
        public string? Parameters { get; set; }

        /// <summary>
        /// 返回值（JSON格式）
        /// </summary>
        public string? ReturnValue { get; set; }

        /// <summary>
        /// 异常消息
        /// </summary>
        public string? ExceptionMessage { get; set; }

        /// <summary>
        /// 异常堆栈
        /// </summary>
        public string? ExceptionStackTrace { get; set; }

        /// <summary>
        /// 状态：0=成功, 1=失败
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 执行时间点
        /// </summary>
        public DateTime ExecutionTime { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreationTime { get; set; }

        /// <summary>
        /// 跟踪ID
        /// </summary>
        public string? TraceId { get; set; }

        /// <summary>
        /// 额外信息（用于存储HTTP级特有的扩展数据）
        /// </summary>
        public Dictionary<string, object> ExtraProperties { get; set; } = new();
    }

    /// <summary>
    /// 审计日志状态枚举
    /// </summary>
    public enum AuditLogStatus
    {
        /// <summary>
        /// 成功
        /// </summary>
        Success = 0,

        /// <summary>
        /// 失败
        /// </summary>
        Failure = 1
    }
}
