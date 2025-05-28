using System.Threading.Tasks;

namespace CrestCreates.Modularity;

/// <summary>
/// 应用程序关闭后的模块钩子接口
/// </summary>
public interface IOnPostApplicationShutdown
{
    /// <summary>
    /// 应用程序关闭后的异步处理
    /// </summary>
    Task OnPostApplicationShutdownAsync();

    /// <summary>
    /// 应用程序关闭后的处理
    /// </summary>
    void OnPostApplicationShutdown();
}
