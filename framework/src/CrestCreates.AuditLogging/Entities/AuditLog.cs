using System;
using System.Collections.Generic;
using CrestCreates.Domain.Entities;

namespace CrestCreates.AuditLogging.Entities
{
    public class AuditLog : Entity<Guid>
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public AuditLog()
        {
            // Id 会在基类中被设置为默认值
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="id">审计日志ID</param>
        public AuditLog(Guid id)
        {
            // 通过反射设置Id属性
            var idProperty = typeof(Entity<Guid>).GetProperty("Id");
            if (idProperty != null && idProperty.CanWrite)
            {
                idProperty.SetValue(this, id);
            }
        }

        /// <summary>
        /// 操作时间
        /// </summary>
        public DateTime CreationTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 操作人ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 操作人名称
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 操作类型
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// 操作描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 操作IP
        /// </summary>
        public string ClientIpAddress { get; set; }

        /// <summary>
        /// 操作设备
        /// </summary>
        public string ClientName { get; set; }

        /// <summary>
        /// 操作路径
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 操作方法
        /// </summary>
        public string HttpMethod { get; set; }

        /// <summary>
        /// 状态码
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// 执行时间（毫秒）
        /// </summary>
        public long ExecutionTime { get; set; }

        /// <summary>
        /// 请求参数
        /// </summary>
        public string Request { get; set; }

        /// <summary>
        /// 响应结果
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// 异常信息
        /// </summary>
        public string Exception { get; set; }

        /// <summary>
        /// 租户ID
        /// </summary>
        public string TenantId { get; set; }

        /// <summary>
        /// 额外信息
        /// </summary>
        public Dictionary<string, object> ExtraProperties { get; set; } = new Dictionary<string, object>();
    }

    public enum AuditLogAction
    {
        Create,
        Update,
        Delete,
        Query,
        Login,
        Logout,
        Other
    }
}