using System;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Infrastructure.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.CodeGenerator.Tests.Modules
{
    /// <summary>
    /// 数据库模块 - 依赖 CoreModule
    /// 演示模块依赖和加载顺序
    /// </summary>
    [Module(typeof(CoreModule))]  // 指定依赖 CoreModule
    public class DatabaseModule : ModuleBase
    {
        private ILogger<DatabaseModule>? _logger;

        public override string Name => "Database Module";
        public override string Description => "提供数据库访问功能";
        public override string Version => "1.0.0";

        public override void OnPreInitialize()
        {
            Console.WriteLine($"[{Name}] PreInitialize - 准备数据库连接");
        }

        public override void OnInitialize()
        {
            Console.WriteLine($"[{Name}] Initialize - 初始化数据库上下文");
        }

        public override void OnPostInitialize()
        {
            Console.WriteLine($"[{Name}] PostInitialize - 验证数据库连接");
        }

        public override void OnConfigureServices(IServiceCollection services)
        {
            Console.WriteLine($"[{Name}] ConfigureServices - 注册数据库服务");

            // 注册数据库相关服务
            services.AddSingleton<IDatabaseContext, DatabaseContext>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
        }

        public override void OnApplicationInitialization(IHost host)
        {
            _logger = host.Services.GetService<ILogger<DatabaseModule>>();
            _logger?.LogInformation($"[{Name}] ApplicationInitialization");

            // 可以访问 CoreModule 提供的服务
            var coreService = host.Services.GetService<ICoreService>();
            if (coreService != null)
            {
                _logger?.LogInformation($"依赖的核心模块: {coreService.GetModuleName()}");
            }

            // 初始化数据库
            var dbContext = host.Services.GetService<IDatabaseContext>();
            dbContext?.Initialize();
        }
    }

    #region 示例服务

    public interface IDatabaseContext
    {
        void Initialize();
        void BeginTransaction();
        void Commit();
    }

    public class DatabaseContext : IDatabaseContext
    {
        private readonly ILogger<DatabaseContext> _logger;

        public DatabaseContext(ILogger<DatabaseContext> logger)
        {
            _logger = logger;
        }

        public void Initialize()
        {
            _logger.LogInformation("数据库上下文初始化完成");
        }

        public void BeginTransaction()
        {
            _logger.LogDebug("开始事务");
        }

        public void Commit()
        {
            _logger.LogDebug("提交事务");
        }
    }

    public interface IUserRepository
    {
        void SaveUser(string username);
    }

    public class UserRepository : IUserRepository
    {
        private readonly IDatabaseContext _dbContext;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(IDatabaseContext dbContext, ILogger<UserRepository> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public void SaveUser(string username)
        {
            _logger.LogInformation($"保存用户: {username}");
            _dbContext.BeginTransaction();
            // 保存逻辑
            _dbContext.Commit();
        }
    }

    public interface IProductRepository
    {
        void SaveProduct(string productName);
    }

    public class ProductRepository : IProductRepository
    {
        private readonly IDatabaseContext _dbContext;

        public ProductRepository(IDatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public void SaveProduct(string productName)
        {
            Console.WriteLine($"保存产品: {productName}");
        }
    }

    #endregion
}
