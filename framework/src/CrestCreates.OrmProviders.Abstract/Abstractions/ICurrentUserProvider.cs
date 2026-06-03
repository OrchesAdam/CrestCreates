using System;

namespace CrestCreates.OrmProviders.Abstract.Abstractions;

/// <summary>
/// 当前用户提供者接口
/// 用于审计字段自动填充当前操作人ID
/// </summary>
public interface ICurrentUserProvider
{
    Guid? GetCurrentUserId();
}

/// <summary>
/// 默认当前用户提供者实现（未注入 HttpContext 时的占位）
/// </summary>
public class DefaultCurrentUserProvider : ICurrentUserProvider
{
    public Guid? GetCurrentUserId()
    {
        return null;
    }
}
