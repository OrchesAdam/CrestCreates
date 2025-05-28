// using System;
// using System.Threading.Tasks;
// using CrestCreates.Modularity;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Options;
//
// namespace CrestCreates.Data
// {
//     // 数据模块实现部分类
//     // 注：移除了Module特性，因为它会自动添加到生成的部分类中
//     public partial class DataModule
//     {
//         // 自定义实现将被移到这个部分类中
//         // 基础实现由源生成器在DataModule.g.cs中生成
//         
//         // 重写配置服务方法，扩展自动生成的实现
//         public override void ConfigureServices(IServiceCollection services)
//         {
//             // 调用基类实现（自动生成的代码）
//             base.ConfigureServices(services);
//             
//             // 添加数据访问服务
//             services.AddScoped<IDataAccess, DataAccess>();
//             
//             // 根据配置添加数据库上下文
//             if (Options != null)
//             {
//                 switch (Options.DatabaseType.ToLower())
//                 {
//                     case "sqlserver":
//                         services.AddDbContext<DataDbContext>(options =>
//                         {
//                             // 配置 SQL Server
//                         });
//                         break;
//                         
//                     case "postgresql":
//                         services.AddDbContext<DataDbContext>(options =>
//                         {
//                             // 配置 PostgreSQL
//                         });
//                         break;
//                         
//                     default:
//                         services.AddDbContext<DataDbContext>(options =>
//                         {
//                             // 默认数据库配置
//                         });
//                         break;
//                 }
//             }
//         }
//         
//         // 重写应用程序初始化前的处理方法
//         public override void OnPreApplicationInitialization()
//         {
//             // 在应用程序初始化前执行自定义逻辑
//             Console.WriteLine("DataModule: 准备初始化数据模块...");
//         }
//         
//         // 重写应用程序初始化后的处理方法
//         public override void OnPostApplicationInitialization()
//         {
//             // 在应用程序初始化后执行自动迁移（如果配置为启用）
//             if (Options?.AutoMigrate == true)
//             {
//                 MigrateDatabase();
//             }
//         }
//         
//         // 实现初始化数据库方法
//         public override async Task InitializeDatabaseAsync()
//         {
//             Console.WriteLine("DataModule: 初始化数据库...");
//             
//             // 初始化数据库的实际逻辑
//             using var scope = _serviceProvider.CreateScope();
//             var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
//             
//             // 确保数据库已创建
//             await dbContext.Database.EnsureCreatedAsync();
//         }
//         
//         // 实现迁移数据库方法
//         public override void MigrateDatabase()
//         {
//             Console.WriteLine("DataModule: 执行数据库迁移...");
//             
//             // 执行迁移的实际逻辑
//             using var scope = _serviceProvider.CreateScope();
//             var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
//             
//             // 应用迁移
//             dbContext.Database.Migrate();
//         }
//         
//         // 实现获取连接字符串方法
//         public override string GetConnectionString()
//         {
//             return Options?.ConnectionString ?? "DefaultConnection";
//         }
//         
//         // 实现数据种子方法
//         public override async Task SeedDataAsync(bool clearExisting = false)
//         {
//             Console.WriteLine($"DataModule: 添加种子数据 (清除现有数据: {clearExisting})");
//             
//             // 数据种子的实际逻辑
//             using var scope = _serviceProvider.CreateScope();
//             var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
//             
//             if (clearExisting)
//             {
//                 // 清除现有数据的逻辑
//             }
//             
//             // 添加种子数据的逻辑
//             await Task.Delay(100); // 模拟异步操作
//         }
//     } 
// }
