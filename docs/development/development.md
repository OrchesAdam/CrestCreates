# CrestCreates 框架 - 开发指南

## 📋 总览

本指南旨在帮助开发者快速上手 CrestCreates 框架，了解框架的核心功能和使用方法，从而高效地构建高质量的应用程序。

## 🚀 快速开始

### 1. 环境准备

- **.NET SDK**：.NET 10.0 或更高版本
- **IDE**：Visual Studio 2022 或更高版本，或 Visual Studio Code
- **数据库**：SQL Server、MySQL、PostgreSQL 等（根据选择的 ORM 而定）
- **可选工具**：Docker（用于运行 RabbitMQ、Redis 等服务）

### 2. 项目初始化

#### 2.1 克隆代码库

```bash
git clone https://github.com/yourusername/CrestCreates.git
cd CrestCreates
```

#### 2.2 恢复依赖

```bash
dotnet restore
```

#### 2.3 构建项目

```bash
dotnet build
```

### 3. 项目结构

CrestCreates 采用模块化的项目结构，主要包含以下部分：

- **framework/src/**：框架核心代码
- **framework/test/**：测试代码
- **framework/tools/**：工具代码（如代码生成器）
- **docs/**：文档

## 🏗️ 核心功能使用指南

### 1. 模块化开发

#### 1.1 创建模块

1. **创建模块项目**：
   - 在 `framework/src` 目录下创建一个新的类库项目
   - 项目名称建议使用 `CrestCreates.{ModuleName}` 格式

2. **实现模块接口**：
   ```csharp
   using CrestCreates.Infrastructure.Modularity;
   
   [Module("ModuleA", Dependencies = new[] { "ModuleB" })]
   public class ModuleA : ModuleBase
   {
       public override void ConfigureServices(IServiceCollection services)
       {
           // 注册模块服务
       }
       
       public override void Configure(IApplicationBuilder app)
       {
           // 配置应用
       }
   }
   ```

3. **注册模块**：
   - 模块会被自动发现和注册，无需手动注册

#### 1.2 模块生命周期

模块有以下生命周期钩子：

1. **Initialize**：模块初始化
2. **ConfigureServices**：配置服务
3. **Configure**：配置应用
4. **Start**：模块启动
5. **Stop**：模块停止

### 2. 领域驱动设计

#### 2.1 定义实体

1. **创建实体类**：
   ```csharp
   using CrestCreates.Domain.Entities;
   using CrestCreates.Domain.DomainEvents;
   
   public class Product : AggregateRoot<int>
   {
       public string Name { get; set; }
       public decimal Price { get; set; }
       public bool IsActive { get; set; }
       
       public void ChangePrice(decimal newPrice)
       {
           Price = newPrice;
           AddDomainEvent(new ProductPriceChangedEvent(this));
       }
   }
   ```

2. **定义领域事件**：
   ```csharp
   public class ProductPriceChangedEvent : DomainEvent
   {
       public Product Product { get; }
       
       public ProductPriceChangedEvent(Product product)
       {
           Product = product;
       }
   }
   ```

3. **实现事件处理器**：
   ```csharp
   using MediatR;
   
   public class ProductPriceChangedEventHandler : INotificationHandler<ProductPriceChangedEvent>
   {
       public async Task Handle(ProductPriceChangedEvent notification, CancellationToken cancellationToken)
       {
           // 处理价格变化事件
       }
   }
   ```

#### 2.2 使用仓储

1. **定义仓储接口**：
   ```csharp
   using CrestCreates.Domain.Repositories;
   
   public interface IProductRepository : IRepository<Product, int>
   {
       Task<Product> GetByNameAsync(string name, CancellationToken cancellationToken = default);
   }
   ```

2. **使用代码生成器**：
   - 代码生成器会自动生成仓储实现

3. **注入和使用仓储**：
   ```csharp
   public class ProductService
   {
       private readonly IProductRepository _productRepository;
       private readonly IUnitOfWork _unitOfWork;
       
       public ProductService(IProductRepository productRepository, IUnitOfWork unitOfWork)
       {
           _productRepository = productRepository;
           _unitOfWork = unitOfWork;
       }
       
       public async Task CreateProductAsync(Product product, CancellationToken cancellationToken = default)
       {
           await _productRepository.AddAsync(product, cancellationToken);
           await _unitOfWork.SaveChangesAsync(cancellationToken);
       }
   }
   ```

### 3. 多 ORM 支持

#### 3.1 选择 ORM

CrestCreates 支持三种 ORM：

1. **Entity Framework Core**：Microsoft 官方 ORM，功能强大，生态丰富
2. **FreeSql**：轻量级 ORM，性能优秀，API 友好
3. **SqlSugar**：高性能 ORM，语法简洁，功能丰富

#### 3.2 配置 ORM

在 `Startup.cs` 中配置 ORM：

```csharp
// 使用 EF Core
services.AddDbContext<CrestCreatesDbContext>(options =>
    options.UseSqlServer(Configuration.GetConnectionString("Default")));
services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();

// 或使用 FreeSql
services.AddFreeSql(options =>
    options.UseConnectionString(Configuration.GetConnectionString("Default"))
        .UseSqlServer());
services.AddScoped<IUnitOfWork, FreeSqlUnitOfWork>();

// 或使用 SqlSugar
services.AddSqlSugar(options =>
    options.ConnectionString = Configuration.GetConnectionString("Default")
        .DbType = SqlSugar.DbType.SqlServer);
services.AddScoped<IUnitOfWork, SqlSugarUnitOfWork>();
```

### 4. 事件总线

#### 4.1 领域事件

**定义领域事件**：

```csharp
using CrestCreates.Domain.DomainEvents;

public class ProductCreatedEvent : DomainEvent
{
    public Guid ProductId { get; }
    public string ProductName { get; }

    public ProductCreatedEvent(Guid productId, string productName)
    {
        ProductId = productId;
        ProductName = productName;
    }
}
```

**在实体中添加领域事件**：

```csharp
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.DomainEvents;

public class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; }
    
    public Product(string name, decimal price)
    {
        Id = Guid.NewGuid();
        Name = name;
        Price = price;
        IsActive = true;
        
        // 添加领域事件
        AddDomainEvent(new ProductCreatedEvent(Id, name));
    }
    
    public void ChangePrice(decimal newPrice)
    {
        if (newPrice <= 0)
            throw new ArgumentException("Price must be greater than zero");
        
        Price = newPrice;
        AddDomainEvent(new ProductPriceChangedEvent(Id, newPrice));
    }
    
    public void Deactivate()
    {
        IsActive = false;
        AddDomainEvent(new ProductDeactivatedEvent(Id));
    }
}
```

**实现事件处理器**：

```csharp
using MediatR;
using System.Threading;
using System.Threading.Tasks;

public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    public async Task Handle(ProductCreatedEvent notification, CancellationToken cancellationToken)
    {
        // 处理产品创建事件，例如发送通知、更新统计数据等
        Console.WriteLine($"Product created: {notification.ProductName} (ID: {notification.ProductId})");
        await Task.CompletedTask;
    }
}

public class ProductPriceChangedEventHandler : INotificationHandler<ProductPriceChangedEvent>
{
    public async Task Handle(ProductPriceChangedEvent notification, CancellationToken cancellationToken)
    {
        // 处理价格变更事件
        Console.WriteLine($"Product price changed: ID={notification.ProductId}, New Price={notification.NewPrice}");
        await Task.CompletedTask;
    }
}
```

**注册事件处理器**：

在 `Startup.cs` 中注册领域事件处理器：

```csharp
using CrestCreates.Infrastructure.EventBus;

// 注册领域事件处理器
services.AddDomainEventHandlers();

// 或指定程序集
services.AddDomainEventHandlers(typeof(ProductCreatedEventHandler).Assembly);
```

**工作单元自动发布领域事件**：

工作单元会在事务提交时自动发布实体中的领域事件：

```csharp
public class ProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    
    public ProductService(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }
    
    public async Task CreateProductAsync(string name, decimal price, CancellationToken cancellationToken = default)
    {
        var product = new Product(name, price);
        await _productRepository.AddAsync(product, cancellationToken);
        
        // 保存变更并自动发布领域事件
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
    
    public async Task UpdateProductPriceAsync(Guid productId, decimal newPrice, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken);
        product.ChangePrice(newPrice);
        
        // 保存变更并自动发布领域事件
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

#### 4.2 分布式事件总线

1. **配置 RabbitMQ**：
   ```csharp
services.AddRabbitMqEventBus(Configuration.GetConnectionString("RabbitMQ"));
```

2. **发布事件**：
   ```csharp
await _eventBus.PublishAsync(new ProductCreatedEvent(product), cancellationToken);
```

3. **订阅事件**：
   ```csharp
_eventBus.Subscribe<ProductCreatedEvent, ProductCreatedEventHandler>();
```

### 5. 缓存系统

#### 5.1 配置缓存

在 `Startup.cs` 中配置缓存：

```csharp
// 使用内存缓存
services.AddCaching(config =>
{
    config.Provider = "memory";
    config.DefaultExpiration = TimeSpan.FromMinutes(30);
});

// 或使用 Redis 缓存
services.AddCaching(config =>
{
    config.Provider = "redis";
    config.RedisConnectionString = Configuration.GetConnectionString("Redis");
    config.DefaultExpiration = TimeSpan.FromMinutes(30);
});
```

#### 5.2 使用缓存

```csharp
public class ProductService
{
    private readonly ICache _cache;
    private readonly IProductRepository _productRepository;
    
    public ProductService(ICache cache, IProductRepository productRepository)
    {
        _cache = cache;
        _productRepository = productRepository;
    }
    
    public async Task<Product> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyGenerator.GenerateProductKey(id);
        var product = await _cache.GetAsync<Product>(cacheKey, cancellationToken);
        
        if (product == null)
        {
            product = await _productRepository.GetAsync(id, cancellationToken);
            await _cache.SetAsync(cacheKey, product, TimeSpan.FromMinutes(10), cancellationToken);
        }
        
        return product;
    }
}
```

### 6. 日志系统

#### 6.1 配置日志

在 `Startup.cs` 中配置日志：

```csharp
services.AddSerilogLogging(config =>
{
    config.MinimumLevel = LogLevel.Information;
    config.EnableConsole = true;
    config.EnableFile = true;
    config.FilePath = "logs/log-.txt";
});
```

#### 6.2 使用日志

```csharp
public class ProductService
{
    private readonly ILogger<ProductService> _logger;
    
    public ProductService(ILogger<ProductService> logger)
    {
        _logger = logger;
    }
    
    public async Task CreateProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating product: {ProductName}", product.Name);
        
        try
        {
            // 创建产品逻辑
            _logger.LogInformation("Product created successfully: {ProductId}", product.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create product: {ProductName}", product.Name);
            throw;
        }
    }
}
```

### 7. 多租户支持

#### 7.1 配置多租户

在 `Startup.cs` 中配置多租户：

```csharp
services.AddMultiTenancy(options =>
{
    options.DefaultTenantId = "default";
    options.TenantResolvers = new List<ITenantResolver>
    {
        new HeaderTenantResolver("X-Tenant-ID"),
        new SubdomainTenantResolver(),
        new QueryStringTenantResolver("tenant")
    };
});

// 配置租户提供者
services.AddScoped<ITenantProvider, ConfigurationTenantProvider>();

// 配置中间件
app.UseMultiTenancy();
```

#### 7.2 使用多租户

```csharp
public class ProductService
{
    private readonly ICurrentTenant _currentTenant;
    
    public ProductService(ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
    }
    
    public async Task CreateProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        // 获取当前租户
        var tenantId = _currentTenant.Id;
        
        // 创建产品逻辑
    }
}
```

### 8. RBAC 授权体系

#### 8.1 配置授权

在 `Startup.cs` 中配置授权：

```csharp
services.AddAuthorization();
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // 其他配置...
        };
    });

// 配置权限存储
services.AddPermissionStore();
```

#### 8.2 定义权限

```csharp
public class PermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(PermissionDefinitionContext context)
    {
        var productGroup = context.AddGroup("Product", "产品管理");
        
        productGroup.AddPermission("Product.Create", "创建产品");
        productGroup.AddPermission("Product.Read", "查看产品");
        productGroup.AddPermission("Product.Update", "更新产品");
        productGroup.AddPermission("Product.Delete", "删除产品");
    }
}
```

#### 8.3 使用授权

```csharp
// 使用特性授权
[AuthorizePermission("Product.Create")]
public async Task<IActionResult> Create(CreateProductDto dto)
{
    // 创建产品逻辑
}

// 使用命令式授权
public async Task<IActionResult> Delete(int id)
{
    var hasPermission = await _permissionChecker.IsGrantedAsync("Product.Delete");
    if (!hasPermission)
    {
        return Forbid();
    }
    
    // 删除产品逻辑
}
```

## 📝 最佳实践

### 1. 代码组织

- **按功能组织代码**：将相关功能组织在同一模块中
- **遵循分层架构**：严格按照领域层、应用层、基础设施层和 API 层的分层结构组织代码
- **使用依赖注入**：通过构造函数注入依赖，避免直接创建依赖对象

### 2. 命名约定

- **类名**：使用 PascalCase，如 `ProductService`
- **方法名**：使用 PascalCase，如 `CreateProductAsync`
- **参数名**：使用 camelCase，如 `productName`
- **私有字段**：使用 `_camelCase`，如 `_productRepository`
- **常量**：使用 `UPPER_SNAKE_CASE`，如 `MAX_PAGE_SIZE`

### 3. 错误处理

- **使用异常**：对于业务逻辑错误，使用异常表示
- **统一异常处理**：在 API 层使用中间件统一处理异常
- **日志记录**：在捕获异常时记录详细的日志信息

### 4. 测试

- **单元测试**：测试领域层和应用层的核心逻辑
- **集成测试**：测试模块间的交互和外部依赖
- **使用测试基类**：使用 `CrestCreates.TestBase` 中的测试基类简化测试代码
- **使用 TestDataBuilder**：使用 `TestDataBuilder` 生成测试数据

### 5. 性能优化

- **使用缓存**：对于频繁访问的数据，使用缓存
- **批量操作**：对于批量数据操作，使用批量 API
- **异步操作**：对于 I/O 密集型操作，使用异步方法
- **数据库索引**：为常用查询添加适当的索引

## 🔧 工具使用

### 1. 代码生成器

CrestCreates 提供了三个代码生成器：

#### 1.1 EntityGenerator

- **功能**：生成实体的仓储接口和实现
- **使用方法**：在实体类上添加 `[Entity]` 特性
- **生成文件**：
  - 仓储接口：`I{EntityName}Repository.cs`
  - EF Core 仓储实现：`EfCore{EntityName}Repository.cs`
  - FreeSql 仓储实现：`FreeSql{EntityName}Repository.cs`
  - SqlSugar 仓储实现：`SqlSugar{EntityName}Repository.cs`

#### 1.2 ServiceGenerator

- **功能**：生成应用服务接口、实现和 API 控制器
- **使用方法**：在服务类上添加 `[Service]` 特性
- **生成文件**：
  - 服务接口：`I{ServiceName}.cs`
  - 服务实现：`{ServiceName}.cs`
  - API 控制器：`{ServiceName}Controller.cs`

#### 1.3 ModuleGenerator

- **功能**：生成模块的注册代码
- **使用方法**：在模块类上添加 `[Module]` 特性
- **生成文件**：
  - 模块注册代码：`ModuleRegistration.cs`

### 2. 测试工具

#### 2.1 TestBase

- **功能**：提供测试基类和辅助方法
- **使用方法**：继承 `TestBase`、`DomainTestBase`、`ApplicationTestBase` 或 `IntegrationTestBase`
- **主要特性**：
  - 集成 AutoFixture，自动生成测试数据
  - 提供服务容器，支持依赖注入
  - 提供 Mock 注册和管理

#### 2.2 TestDataBuilder

- **功能**：生成测试数据
- **使用方法**：使用 `TestDataBuilder.For<T>()` 创建构建器
- **示例**：
  ```csharp
  var product = TestDataBuilder.For<Product>()
      .With(p => p.Name = "Test Product")
      .With(p => p.Price = 99.99)
      .Build();
  ```

## 📚 常见问题

### 1. 模块依赖问题

**问题**：模块间出现循环依赖

**解决方案**：
- 检查模块依赖关系，确保没有循环依赖
- 提取共享功能到独立模块
- 重新设计模块边界

### 2. ORM 切换问题

**问题**：切换 ORM 后代码无法编译

**解决方案**：
- 确保使用了 ORM 抽象接口
- 检查是否有直接依赖于特定 ORM 的代码
- 使用代码生成器重新生成仓储实现

### 3. 事件总线问题

**问题**：事件未被处理

**解决方案**：
- 检查事件处理器是否正确注册
- 确保事件类型和处理器类型匹配
- 检查事件总线配置是否正确

### 4. 缓存一致性问题

**问题**：缓存数据与数据库不一致

**解决方案**：
- 在数据变更时更新或清除缓存
- 使用缓存过期策略
- 考虑使用分布式缓存

### 5. 多租户数据隔离问题

**问题**：租户数据混合

**解决方案**：
- 确保使用了多租户中间件
- 检查数据库查询是否包含租户过滤
- 验证租户解析器是否正确配置

## 🚀 部署指南

### 1. 本地开发

1. **启动依赖服务**：
   - 启动数据库（如 SQL Server）
   - 启动 RabbitMQ（如果使用分布式事件总线）
   - 启动 Redis（如果使用 Redis 缓存）

2. **配置连接字符串**：
   - 在 `appsettings.json` 中配置数据库、RabbitMQ 和 Redis 的连接字符串

3. **运行应用**：
   ```bash
   dotnet run --project framework/src/CrestCreates.Web
   ```

### 2. 生产部署

1. **构建发布包**：
   ```bash
   dotnet publish -c Release -o publish
   ```

2. **部署到 IIS**：
   - 创建 IIS 应用池
   - 创建 IIS 网站，指向发布目录
   - 配置应用池为无托管代码

3. **部署到 Docker**：
   - 创建 `Dockerfile`
   - 构建 Docker 镜像
   - 运行 Docker 容器

4. **配置环境变量**：
   - 在生产环境中使用环境变量覆盖配置

## 📄 许可证

CrestCreates 框架采用 MIT 许可证，详见 LICENSE 文件。
