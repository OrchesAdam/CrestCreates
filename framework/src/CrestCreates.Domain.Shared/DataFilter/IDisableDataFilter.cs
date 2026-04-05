using System;

namespace CrestCreates.Domain.Shared.DataFilter;

/// <summary>
/// 数据过滤器禁用接口
/// 提供数据过滤器的启用、禁用和状态检查功能
/// </summary>
public interface IDisableDataFilter
{
    /// <summary>
    /// 检查指定过滤器是否启用
    /// </summary>
    /// <typeparam name="TFilter">过滤器类型</typeparam>
    /// <returns>如果过滤器启用返回true，否则返回false</returns>
    bool IsEnabled<TFilter>() where TFilter : class;

    /// <summary>
    /// 禁用指定过滤器
    /// </summary>
    /// <typeparam name="TFilter">过滤器类型</typeparam>
    /// <returns>用于恢复过滤器状态的可释放对象</returns>
    IDisposable Disable<TFilter>() where TFilter : class;

    /// <summary>
    /// 启用指定过滤器
    /// </summary>
    /// <typeparam name="TFilter">过滤器类型</typeparam>
    /// <returns>用于恢复过滤器状态的可释放对象</returns>
    IDisposable Enable<TFilter>() where TFilter : class;
}