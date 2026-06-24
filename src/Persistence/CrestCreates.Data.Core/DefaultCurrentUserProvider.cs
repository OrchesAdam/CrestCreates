using System;
using CrestCreates.Data.Abstractions;

namespace CrestCreates.Data.Core;

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
