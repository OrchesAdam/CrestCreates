# ModuleGenerator 测试示例

本目录包含用于测试 ModuleGenerator 功能的示例模块。

## 模块列表

### 1. CoreModule.cs
**功能**：核心基础模块，无依赖  
**特性**：
- ✅ 无依赖，作为其他模块的基础
- ✅ 提供核心服务（ICoreService, ICoreRepository）
- ✅ 配置日志系统
- ✅ 演示所有生命周期钩子

**生命周期**：
```
OnPreInitialize    → 准备核心服务
OnInitialize       → 初始化核心组件
OnPostInitialize   → 完成初始化
OnConfigureServices → 注册核心服务
OnApplicationInitialization → 启动核心服务
```

### 2. DatabaseModule.cs
**功能**：数据库模块，依赖 CoreModule  
**特性**：
- ✅ 依赖 CoreModule
- ✅ 提供数据库上下文和仓储
- ✅ 演示模块依赖关系
- ✅ 访问依赖模块的服务

**依赖关系**：
```
[Module(typeof(CoreModule))]  // 指定依赖
```

**提供的服务**：
- `IDatabaseContext` - 数据库上下文
- `IUserRepository` - 用户仓储
- `IProductRepository` - 产品仓储

### 3. ApplicationModule.cs
**功能**：应用模块，依赖 CoreModule 和 DatabaseModule  
**特性**：
- ✅ 多模块依赖
- ✅ 提供业务服务
- ✅ 演示依赖注入链
- ✅ 验证模块加载顺序

**依赖关系**：
```
[Module(typeof(CoreModule), typeof(DatabaseModule))]  // 多依赖
```

**提供的服务**：
- `IOrderService` - 订单服务
- `IInventoryService` - 库存服务
- `INotificationService` - 通知服务

## 模块依赖图

```
CoreModule (基础模块)
    ↓
DatabaseModule (依赖 CoreModule)
    ↓
ApplicationModule (依赖 CoreModule, DatabaseModule)
```

## 模块加载顺序

根据拓扑排序，模块将按以下顺序加载：

1. **CoreModule** - 首先加载，无依赖
2. **DatabaseModule** - 其次加载，依赖 CoreModule
3. **ApplicationModule** - 最后加载，依赖前两者

## 生命周期阶段

每个模块都会经历以下阶段（按顺序）：

### 阶段 1: PreInitialize
```csharp
// CoreModule
coreModule.OnPreInitialize();

// DatabaseModule
databaseModule.OnPreInitialize();

// ApplicationModule
applicationModule.OnPreInitialize();
```

### 阶段 2: Initialize
```csharp
// 按照依赖顺序
coreModule.OnInitialize();
databaseModule.OnInitialize();
applicationModule.OnInitialize();
```

### 阶段 3: PostInitialize
```csharp
// 按照依赖顺序
coreModule.OnPostInitialize();
databaseModule.OnPostInitialize();
applicationModule.OnPostInitialize();
```

### 阶段 4: ConfigureServices
```csharp
// 向 DI 容器注册服务
coreModule.OnConfigureServices(services);
databaseModule.OnConfigureServices(services);
applicationModule.OnConfigureServices(services);
```

### 阶段 5: ApplicationInitialization
```csharp
// 应用启动后，可以访问完整的 DI 容器
coreModule.OnApplicationInitialization(host);
databaseModule.OnApplicationInitialization(host);
applicationModule.OnApplicationInitialization(host);
```

## 预期生成的代码

### AutoModuleRegistration.g.cs
```csharp
public static class AutoModuleRegistration
{
    // 已注册的模块列表（按加载顺序）
    public static readonly IReadOnlyList<string> RegisteredModules = new[]
    {
        "CrestCreates.CodeGenerator.Tests.Modules.CoreModule",
        "CrestCreates.CodeGenerator.Tests.Modules.DatabaseModule",
        "CrestCreates.CodeGenerator.Tests.Modules.ApplicationModule",
    };

    // 注册所有模块到 HostBuilder
    public static IHostBuilder RegisterModules(this IHostBuilder hostBuilder)
    {
        // 按照依赖顺序注册和初始化模块
        // ...
    }

    // 初始化所有模块
    public static IHost InitializeModules(this IHost host)
    {
        // 应用启动后调用每个模块的 OnApplicationInitialization
        // ...
    }
}
```

### 模块扩展方法
为每个模块生成扩展方法：

- `CoreModuleExtensions.g.cs`
- `DatabaseModuleExtensions.g.cs`
- `ApplicationModuleExtensions.g.cs`

```csharp
public static class CoreModuleExtensions
{
    public static CoreModule GetCoreModule(this IServiceProvider services)
    {
        return services.GetRequiredService<CoreModule>();
    }
}
```

## 使用示例

### 在 Program.cs 中使用

```csharp
using CrestCreates.Infrastructure.Modularity;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args);

// 注册所有模块
builder.RegisterModules();

var host = builder.Build();

// 初始化所有模块
host.InitializeModules();

await host.RunAsync();
```

### 访问模块

```csharp
// 通过 DI 容器访问
var coreModule = host.Services.GetService<CoreModule>();

// 使用扩展方法访问
var coreModule = host.Services.GetCoreModule();
```

### 访问模块提供的服务

```csharp
// 访问 CoreModule 提供的服务
var coreService = host.Services.GetService<ICoreService>();
coreService.Initialize();

// 访问 DatabaseModule 提供的服务
var userRepo = host.Services.GetService<IUserRepository>();
userRepo.SaveUser("张三");

// 访问 ApplicationModule 提供的服务
var orderService = host.Services.GetService<IOrderService>();
orderService.CreateOrder("ORD-001");
```

## 编译和测试

### 1. 编译项目
```bash
dotnet build test/CrestCreates.CodeGenerator.Tests
```

### 2. 查看生成的文件
生成的文件位于：
```
test/CrestCreates.CodeGenerator.Tests/obj/Debug/net10.0/generated/
CrestCreates.CodeGenerator.ModuleGenerator/
├── AutoModuleRegistration.g.cs
├── CoreModuleExtensions.g.cs
├── DatabaseModuleExtensions.g.cs
└── ApplicationModuleExtensions.g.cs
```

### 3. 运行时输出

预期控制台输出（按模块加载顺序）：

```
[Core Module] PreInitialize - 准备核心服务
[Database Module] PreInitialize - 准备数据库连接
[Application Module] PreInitialize - 准备应用程序组件

[Core Module] Initialize - 初始化核心组件
[Database Module] Initialize - 初始化数据库上下文
[Application Module] Initialize - 初始化应用程序

[Core Module] PostInitialize - 完成初始化
[Database Module] PostInitialize - 验证数据库连接
[Application Module] PostInitialize - 应用程序就绪

[Core Module] ConfigureServices - 注册核心服务
[Database Module] ConfigureServices - 注册数据库服务
[Application Module] ConfigureServices - 注册应用服务

All modules registered successfully

[Core Module] ApplicationInitialization - 应用程序启动
[Database Module] ApplicationInitialization
[Application Module] ApplicationInitialization

All modules initialized successfully
```

## 特性验证

### ✅ 依赖解析
- CoreModule 无依赖，首先加载
- DatabaseModule 依赖 CoreModule，在其后加载
- ApplicationModule 依赖两者，最后加载

### ✅ 拓扑排序
- 自动按照依赖关系排序
- 检测循环依赖并报错

### ✅ 生命周期管理
- 5 个生命周期钩子按顺序执行
- 每个阶段所有模块都执行完才进入下一阶段

### ✅ 服务注册
- 每个模块在 OnConfigureServices 中注册服务
- 服务可以被其他模块和应用代码访问

### ✅ 日志记录
- 每个阶段都记录日志
- 便于调试模块加载过程

## 高级用法

### 自定义模块加载顺序

```csharp
[Module(
    typeof(CoreModule),
    typeof(DatabaseModule),
    Order = -10  // 负数表示高优先级
)]
public class PriorityModule : ModuleBase
{
    // ...
}
```

### 禁用自动服务注册

```csharp
[Module(AutoRegisterServices = false)]
public class ManualModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        // 手动注册服务
    }
}
```

### 条件化模块加载

```csharp
public class ConditionalModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        if (environment == "Development")
        {
            // 只在开发环境注册某些服务
            services.AddScoped<IDevService, DevService>();
        }
    }
}
```

## 错误处理

### 循环依赖检测

如果出现循环依赖：
```csharp
[Module(typeof(ModuleB))]
public class ModuleA : ModuleBase { }

[Module(typeof(ModuleA))]  // ❌ 循环依赖！
public class ModuleB : ModuleBase { }
```

将会抛出异常：
```
InvalidOperationException: Circular dependency detected involving module: ModuleA
```

### 缺失依赖

如果依赖的模块未找到，该依赖将被忽略（不会影响其他模块加载）。

## 总结

ModuleGenerator 提供了：

1. **自动模块注册** - 无需手动注册模块
2. **依赖管理** - 自动解析和排序模块依赖
3. **生命周期管理** - 5 个清晰的生命周期钩子
4. **拓扑排序** - 保证模块按正确顺序加载
5. **循环检测** - 自动检测并报告循环依赖
6. **类型安全** - 编译时验证，无运行时反射
7. **便利扩展** - 自动生成模块访问扩展方法

这使得构建大型模块化应用程序变得简单而安全！
