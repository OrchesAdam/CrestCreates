using System.Threading.Tasks;

namespace CrestCreates.Modularity;

/// <summary>
/// 应用程序初始化后的模块钩子接口
/// </summary>
public interface IOnPostApplicationInitialization
{
    /// <summary>
    /// 应用程序初始化后的异步处理
    /// </summary>
    Task OnPostApplicationInitializationAsync();

    /// <summary>
    /// 应用程序初始化后的处理
    /// </summary>
    void OnPostApplicationInitialization();
}
