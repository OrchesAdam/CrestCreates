// using System;
// using CrestCreates.Data;
// using CrestCreates.Modularity;
// using Microsoft.Extensions.DependencyInjection;
//
// namespace CrestCreates.Examples
// {
//     public class DataModuleExample
//     {
//         public static void ConfigureServices()
//         {
//             // 创建服务集合
//             var services = new ServiceCollection();
//
//             // 注册所有模块
//             services.AddModules();
//
//             // 配置 DataModule 的选项
//             services.Configure<DataModuleOptions>(options =>
//             {
//                 options.ConnectionString = "Server=localhost;Database=CrestCreates;User Id=sa;Password=YourPassword;";
//                 options.AutoMigrate = true;
//                 options.DatabaseType = "SqlServer";
//             });
//
//             // 配置模块服务
//             services.ConfigureModules();
//
//             // 构建服务提供程序
//             var serviceProvider = services.BuildServiceProvider();
//
//             // 初始化模块
//             GeneratedModuleRegistrar.InitializeModules(serviceProvider);
//             
//             // 执行应用程序前置初始化
//             GeneratedModuleRegistrar.PreAppInitialize(serviceProvider);
//             
//             // 业务逻辑
//             // ...
//             
//             // 执行应用程序后置初始化
//             GeneratedModuleRegistrar.PostAppInitialize(serviceProvider);
//             
//             // 访问数据模块
//             var dataModule = serviceProvider.GetRequiredService<IDataModule>();
//             Console.WriteLine($"数据库连接字符串: {dataModule.GetConnectionString()}");
//             
//             // 使用数据访问服务
//             var dataAccess = serviceProvider.GetRequiredService<IDataAccess>();
//             // 数据操作
//             
//             // 应用关闭时
//             GeneratedModuleRegistrar.PreAppShutdown(serviceProvider);
//         }
//     }
// }
