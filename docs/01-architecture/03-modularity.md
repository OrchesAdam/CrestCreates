# 模块化设计

本文档介绍 CrestCreates 框架的模块化设计。

## 概述

模块化设计是一种将系统划分为独立、可复用模块的架构模式。CrestCreates 提供了完整的模块化支持，帮助开发者构建可维护、可扩展的应用。

## 核心概念

### 模块（Module）

模块是功能的独立单元，包含相关的领域逻辑、应用服务和基础设施实现。

**特点**：
- 独立部署（可选）
- 明确依赖关系
- 独立生命周期
- 可复用

### 模块类型

1. **核心模块（Core Module）**
   - 提供框架核心功能
   - 被其他模块依赖
   - 如：Domain、Application、Infrastructure

2. **功能模块（Feature Module）**
   - 实现特定业务功能
   - 可独立开发、测试、部署
   - 如：ProductModule、OrderModule

3. **基础设施模块（Infrastructure Module）**
   - 提供技术实现
   - 可替换的底层实现
   - 如：EFCoreModule、RedisCacheModule

## 模块定义

### 模块接口

```csharp
public interface IModule
{
    /// <summary>
    /// 配置服务
    /// </summary>
    void ConfigureServices(IServiceCollection services);
    
    /// <summary>
    /// 配置应用
    /// </summary>
    void Configure(IApplicationBuilder app);
    
    /// <summary>
    /// 模块启动
    /// </summary>
    Task OnStartupAsync(IServiceProvider serviceProvider);
    
    /// <summary>
    /// 模块停止
    /// </summary>
    Task OnShutdownAsync(IServiceProvider serviceProvider);
}
```

### 模块基类

```csharp
public abstract class ModuleBase : IModule
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }
    
    public virtual void Configure(IApplicationBuilder app)
    {
    }
    
    public virtual Task OnStartupAsync(IServiceProvider serviceProvider)
    {
        return Task.CompletedTask;
    }
    
    public virtual Task OnShutdownAsync(IServiceProvider serviceProvider)
    {
        return Task.CompletedTask;
    }
}
```

### 模块特性

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CrestModuleAttribute : Attribute
{
    public Type[] Dependencies { get; }
    
    public CrestModuleAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies;
    }
}
```

## 创建模块

### 1. 定义模块

```csharp
[Module(typeof(CrestCreatesDomainModule))]
[Module(typeof(CrestCreatesApplicationModule))]
public class ProductModule : ModuleBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册领域服务
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductService, ProductService>();
        
        // 注册事件处理器
        services.AddScoped<IDomainEventHandler<ProductCreatedEvent>, ProductCreatedEventHandler>();
    }
    
    public override void Configure(IApplicationBuilder app)
    {
        // 配置中间件
        app.UseMiddleware<ProductMiddleware>();
    }
    
    public override async Task OnStartupAsync(IServiceProvider serviceProvider)
    {
        // 初始化数据
        var dataSeeder = serviceProvider.GetRequiredService<ProductDataSeeder>();
        await dataSeeder.SeedAsync();
    }
}
```

### 2. 模块项目结构

```
ProductModule/
├── ProductModule.Domain/           # 领域层
│   ├── Entities/
│   │   └── Product.cs
│   ├── Events/
│   │   └── ProductCreatedEvent.cs
│   └── Repositories/
│       └── IProductRepository.cs
├── ProductModule.Application/      # 应用层
│   ├── Services/
│   │   └── ProductService.cs
│   └── Dtos/
│       └── ProductDto.cs
├── ProductModule.Infrastructure/   # 基础设施层
│   ├── Repositories/
│   │   └── ProductRepository.cs
│   └── EntityFramework/
│       └── ProductDbContext.cs
└── ProductModule.Web/              # Web层
    └── Controllers/
        └── ProductsController.cs
```

## 模块生命周期

### 1. 发现阶段

框架扫描并发现所有模块：

```csharp
var moduleTypes = AssemblyHelper.GetAllTypes()
    .Where(t => t.IsClass && !t.IsAbstract && typeof(IModule).IsAssignableFrom(t))
    .ToList();
```

### 2. 排序阶段

根据依赖关系进行拓扑排序：

```csharp
var sortedModules = ModuleDependencySorter.Sort(moduleTypes);
```

### 3. 配置服务阶段

调用每个模块的 `ConfigureServices` 方法：

```csharp
foreach (var module in sortedModules)
{
    module.ConfigureServices(services);
}
```

### 4. 配置应用阶段

调用每个模块的 `Configure` 方法：

```csharp
foreach (var module in sortedModules)
{
    module.Configure(app);
}
```

### 5. 启动阶段

调用每个模块的 `OnStartupAsync` 方法：

```csharp
foreach (var module in sortedModules)
{
    await module.OnStartupAsync(serviceProvider);
}
```

### 6. 停止阶段

应用关闭时调用 `OnShutdownAsync` 方法：

```csharp
foreach (var module in sortedModules.Reverse())
{
    await module.OnShutdownAsync(serviceProvider);
}
```

## 模块依赖管理

### 声明依赖

```csharp
[Module(typeof(CoreModule))]
[Module(typeof(InfrastructureModule))]
public class ProductModule : ModuleBase
{
    // 模块实现
}
```

### 依赖解析

框架自动解析依赖关系并进行拓扑排序：

```
ProductModule
    ├── CoreModule
    └── InfrastructureModule
        └── CoreModule
```

排序结果：`CoreModule -> InfrastructureModule -> ProductModule`

### 循环依赖检测

框架自动检测循环依赖并抛出异常：

```csharp
// 错误示例：循环依赖
[Module(typeof(ModuleB))]
public class ModuleA : ModuleBase { }

[Module(typeof(ModuleA))]
public class ModuleB : ModuleBase { }
// 抛出异常：检测到循环依赖
```

## 模块间通信

### 1. 领域事件

```csharp
// 定义领域事件
public class ProductCreatedEvent : DomainEvent
{
    public Guid ProductId { get; }
    
    public ProductCreatedEvent(Guid productId)
    {
        ProductId = productId;
    }
}

// 在模块 A 中发布事件
public class ProductService : IProductService
{
    public async Task<ProductDto> CreateAsync(CreateProductInput input)
    {
        var product = new Product(input.Name, input.Price);
        await _repository.InsertAsync(product);
        
        // 发布领域事件
        await _eventBus.PublishAsync(new ProductCreatedEvent(product.Id));
        
        return MapToDto(product);
    }
}

// 在模块 B 中处理事件
public class ProductCreatedEventHandler : IDomainEventHandler<ProductCreatedEvent>
{
    public async Task HandleAsync(ProductCreatedEvent eventData)
    {
        // 处理产品创建事件
        await _inventoryService.InitializeInventoryAsync(eventData.ProductId);
    }
}
```

### 2. 共享接口

```csharp
// 在共享层定义接口
public interface IProductService
{
    Task<ProductDto> GetAsync(Guid id);
}

// 模块 A 实现接口
public class ProductService : IProductService
{
    // 实现
}

// 模块 B 使用接口
public class OrderService : IOrderService
{
    private readonly IProductService _productService;
    
    public OrderService(IProductService productService)
    {
        _productService = productService;
    }
}
```

## 最佳实践

### 1. 合理划分模块

- 根据业务功能划分模块
- 保持模块内聚性
- 避免模块间紧耦合

### 2. 明确依赖关系

- 使用 `CrestModuleAttribute` 声明依赖
- 避免循环依赖
- 保持依赖关系简单

### 3. 模块自包含

- 模块应包含完整的功能实现
- 避免跨模块直接引用实现
- 通过接口和事件进行通信

### 4. 配置分离

- 将配置放在模块内部
- 提供配置选项供外部调整
- 使用 Options 模式

```csharp
public class ProductModuleOptions
{
    public bool EnableCaching { get; set; } = true;
    public int CacheExpirationMinutes { get; set; } = 30;
}

public class ProductModule : ModuleBase
{
    private readonly ProductModuleOptions _options;
    
    public ProductModule(IOptions<ProductModuleOptions> options)
    {
        _options = options.Value;
    }
    
    public override void ConfigureServices(IServiceCollection services)
    {
        if (_options.EnableCaching)
        {
            services.AddScoped<IProductService, CachedProductService>();
        }
        else
        {
            services.AddScoped<IProductService, ProductService>();
        }
    }
}
```

### 5. 模块测试

- 为每个模块编写单元测试
- 编写模块集成测试
- 测试模块生命周期

## 相关文档

- [创建模块](../04-modules/01-creating-modules.md) - 模块开发指南
- [模块生命周期](../04-modules/02-module-lifecycle.md) - 生命周期详解
- [依赖管理](../04-modules/03-dependency-management.md) - 依赖管理指南
