using System;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.CodeGenerator.Tests.Modules
{
    /// <summary>
    /// 应用模块 - 依赖 CoreModule 和 DatabaseModule
    /// 演示多模块依赖和拓扑排序
    /// </summary>
    [CrestModule(typeof(CoreModule), typeof(DatabaseModule))]  // 依赖多个模块
    public class ApplicationModule : ModuleBase
    {
        private ILogger<ApplicationModule>? _logger;

        public override string Name => "Application Module";
        public override string Description => "应用程序主模块，提供业务逻辑";
        public override string Version => "1.0.0";

        public override void OnPreInitialize()
        {
            Console.WriteLine($"[{Name}] PreInitialize - 准备应用程序组件");
        }

        public override void OnInitialize()
        {
            Console.WriteLine($"[{Name}] Initialize - 初始化应用程序");
        }

        public override void OnPostInitialize()
        {
            Console.WriteLine($"[{Name}] PostInitialize - 应用程序就绪");
        }

        public override void OnConfigureServices(IServiceCollection services)
        {
            Console.WriteLine($"[{Name}] ConfigureServices - 注册应用服务");

            // 注册应用层服务
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddSingleton<INotificationService, NotificationService>();
        }

        public override void OnApplicationInitialization(IHost host)
        {
            _logger = host.Services.GetService<ILogger<ApplicationModule>>();
            _logger?.LogInformation($"[{Name}] ApplicationInitialization");

            // 验证所有依赖的模块都已加载
            var coreService = host.Services.GetService<ICoreService>();
            var dbContext = host.Services.GetService<IDatabaseContext>();

            if (coreService != null && dbContext != null)
            {
                _logger?.LogInformation("所有依赖模块已成功加载");
                _logger?.LogInformation($"核心模块: {coreService.GetModuleName()}");
            }
            else
            {
                _logger?.LogWarning("某些依赖模块未正确加载");
            }

            // 启动通知服务
            var notificationService = host.Services.GetService<INotificationService>();
            notificationService?.Start();
        }
    }

    #region 示例服务

    public interface IOrderService
    {
        void CreateOrder(string orderNumber);
        void ProcessOrder(string orderNumber);
    }

    public class OrderService : IOrderService
    {
        private readonly IUserRepository _userRepository;
        private readonly IProductRepository _productRepository;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            IUserRepository userRepository,
            IProductRepository productRepository,
            ILogger<OrderService> logger)
        {
            _userRepository = userRepository;
            _productRepository = productRepository;
            _logger = logger;
        }

        public void CreateOrder(string orderNumber)
        {
            _logger.LogInformation($"创建订单: {orderNumber}");
            // 使用来自 DatabaseModule 的仓储
        }

        public void ProcessOrder(string orderNumber)
        {
            _logger.LogInformation($"处理订单: {orderNumber}");
        }
    }

    public interface IInventoryService
    {
        void CheckStock(string productId);
        void UpdateStock(string productId, int quantity);
    }

    public class InventoryService : IInventoryService
    {
        private readonly IProductRepository _productRepository;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(
            IProductRepository productRepository,
            ILogger<InventoryService> logger)
        {
            _productRepository = productRepository;
            _logger = logger;
        }

        public void CheckStock(string productId)
        {
            _logger.LogDebug($"检查库存: {productId}");
        }

        public void UpdateStock(string productId, int quantity)
        {
            _logger.LogInformation($"更新库存: {productId}, 数量: {quantity}");
        }
    }

    public interface INotificationService
    {
        void Start();
        void SendNotification(string message);
    }

    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(ILogger<NotificationService> logger)
        {
            _logger = logger;
        }

        public void Start()
        {
            _logger.LogInformation("通知服务已启动");
        }

        public void SendNotification(string message)
        {
            _logger.LogInformation($"发送通知: {message}");
        }
    }

    #endregion
}
