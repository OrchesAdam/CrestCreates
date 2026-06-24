using System;

namespace CrestCreates.Data.Abstractions;

/// <summary>
/// 当前用户提供者接口
/// 用于审计字段自动填充当前操作人ID
/// </summary>
public interface ICurrentUserProvider
{
    Guid? GetCurrentUserId();
}
