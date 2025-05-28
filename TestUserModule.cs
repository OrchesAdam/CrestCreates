using CrestCreates.Modularity;
using System.Threading.Tasks;

namespace CrestCreates.Test
{
    /// <summary>
    /// 测试用户模块接口
    /// </summary>
    [ModuleInterfaceAttribute(ConfigurationType = typeof(TestUserModuleOptions))]
    public interface ITestUserModule : ICrestCreatesModule
    {
        /// <summary>
        /// 获取用户名
        /// </summary>
        Task<string> GetUserNameAsync(int userId);
        
        /// <summary>
        /// 记录活动
        /// </summary>
        void LogActivity(string message);
        
        /// <summary>
        /// 验证用户
        /// </summary>
        bool ValidateUser(string username, string password);
    }

    /// <summary>
    /// 测试用户模块配置选项
    /// </summary>
    public class TestUserModuleOptions
    {
        public string DatabaseConnectionString { get; set; } = string.Empty;
        public bool EnableLogging { get; set; } = true;
        public int MaxRetryCount { get; set; } = 3;
    }
}
