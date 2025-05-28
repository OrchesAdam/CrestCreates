using System.Threading.Tasks;

namespace CrestCreates.Modularity;

/// <summary>
/// 应用程序关闭前的模块钩子接口
/// </summary>
public interface IOnPreApplicationShutdown
{
    /// <summary>
    /// 应用程序关闭前的异步处理
    /// </summary>
    Task OnPreApplicationShutdownAsync();

    /// <summary>
    /// 应用程序关闭前的处理
    /// </summary>
    void OnPreApplicationShutdown();
}
