using System;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.CodeGenerator.Tests.Modules
{
    /// <summary>
    /// 核心模块 - 无依赖的基础模块
    /// 演示模块的基本使用和生命周期钩子
    /// </summary>
    [CrestModule]
    public class CoreModule : ModuleBase
    {
        private ILogger<CoreModule>? _logger;

        public override string Name => "Core Module";
        public override string Description => "提供核心功能的基础模块";
        public override string Version => "1.0.0";

        public override void OnPreInitialize()
        {
            // 模块预初始化
            // 此时 DI 容器还未完全配置，主要用于准备工作
            Console.WriteLine($"[{Name}] PreInitialize - 准备核心服务");
        }

        public override void OnInitialize()
        {
            // 模块初始化
            // 执行模块的主要初始化逻辑
            Console.WriteLine($"[{Name}] Initialize - 初始化核心组件");
        }

        public override void OnPostInitialize()
        {
            // 模块后初始化
            // 可以访问其他已初始化的模块
            Console.WriteLine($"[{Name}] PostInitialize - 完成初始化");
        }

        public override void OnConfigureServices(IServiceCollection services)
        {
            // 配置服务
            // 向 DI 容器注册此模块提供的服务
            Console.WriteLine($"[{Name}] ConfigureServices - 注册核心服务");

            // 注册模块自己的服务
            services.AddSingleton<ICoreService, CoreService>();
            services.AddScoped<ICoreRepository, CoreRepository>();

            // 注册日志
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
        }

        public override void OnApplicationInitialization(IHost host)
        {
            // 应用程序初始化
            // 在应用程序启动后执行，可以访问完整的 DI 容器
            _logger = host.Services.GetService<ILogger<CoreModule>>();
            _logger?.LogInformation($"[{Name}] ApplicationInitialization - 应用程序启动");

            // 可以在这里执行一些启动后的任务
            var coreService = host.Services.GetService<ICoreService>();
            coreService?.Initialize();
        }
    }

    #region 示例服务

    public interface ICoreService
    {
        void Initialize();
        string GetModuleName();
    }

    public class CoreService : ICoreService
    {
        private readonly ILogger<CoreService> _logger;

        public CoreService(ILogger<CoreService> logger)
        {
            _logger = logger;
        }

        public void Initialize()
        {
            _logger.LogInformation("CoreService initialized");
        }

        public string GetModuleName()
        {
            return "Core Module";
        }
    }

    public interface ICoreRepository
    {
        void SaveData(string data);
    }

    public class CoreRepository : ICoreRepository
    {
        public void SaveData(string data)
        {
            Console.WriteLine($"CoreRepository: Saving data - {data}");
        }
    }

    #endregion
}
