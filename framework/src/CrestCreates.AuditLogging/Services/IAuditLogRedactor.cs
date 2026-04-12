using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;

namespace CrestCreates.AuditLogging.Services;

/// <summary>
/// 统一审计日志脱敏服务接口
/// 负责在 AuditLog 落库前对所有敏感字段进行统一脱敏处理
/// </summary>
public interface IAuditLogRedactor
{
    /// <summary>
    /// 对审计上下文中的敏感字段进行脱敏
    /// </summary>
    /// <param name="context">原始审计上下文（将被就地修改）</param>
    Task RedactAsync(AuditContext context);
}
