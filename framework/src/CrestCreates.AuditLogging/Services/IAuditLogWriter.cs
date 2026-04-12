using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;

namespace CrestCreates.AuditLogging.Services
{
    /// <summary>
    /// 统一审计日志写入器接口
    /// 确保请求级和方法级审计最终只写入一条统一的AuditLog记录
    /// </summary>
    public interface IAuditLogWriter
    {
        /// <summary>
        /// 写入审计日志（由中间件在请求结束时调用）
        /// </summary>
        /// <param name="context">审计上下文</param>
        /// <returns></returns>
        Task WriteAsync(AuditContext context);
    }
}
