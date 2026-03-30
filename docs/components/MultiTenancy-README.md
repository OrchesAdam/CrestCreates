# 多租户 (Multi-Tenancy) 使用指南

## 📖 目录

1. [概述](#概述)
2. [快速开始](#快速开始)
3. [租户隔离策略](#租户隔离策略)
4. [租户识别策略](#租户识别策略)
5. [配置选项](#配置选项)
6. [使用示例](#使用示例)
7. [高级用法](#高级用法)
8. [最佳实践](#最佳实践)

---

## 概述

CrestCreates 框架提供了完整的多租户支持，包括：

### ✨ 核心功能

- **多种隔离策略**
  - 🗄️ **Database** - 每个租户独立数据库
  - 🏷️ **Discriminator** - 共享数据库，使用租户ID字段隔离
  - 📁 **Schema** - 每个租户独立 Schema

- **灵活的租户识别**
  - 📋 HTTP Header
  - 🌐 子域名 (Subdomain)
  - 🔗 查询字符串 (QueryString)
  - 🍪 Cookie
  - 🛤️ 路由参数 (Route)

- **自动数据隔离**
  - EF Core 全局查询过滤器
  - 自动设置租户ID
  - 防止跨租户数据访问

- **租户数据源**
  - 内存提供者（开发/测试）
  - 配置文件提供者
  - 自定义数据库提供者

---

## 快速开始

### 1. 注册服务

#### 方式一：使用内存提供者（推荐用于开发）

```csharp
// Program.cs 或 Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    services.AddMultiTenancyWithInMemory(
        // 配置选项
        options =>
        {
            options.ResolutionStrategy = TenantResolutionStrategy.Header;
            options.IsolationStrategy = TenantIsolationStrategy.Database;
            options.TenantHeaderName = "X-Tenant-Id";
        },
        // 配置租户数据
        provider =>
        {
            provider.AddTenant(new TenantInfo(
                id: "tenant1",
                name: "Tenant 1",
                connectionString: "Server=...;Database=Tenant1Db;..."));

            provider.AddTenant(new TenantInfo(
                id: "tenant2",
                name: "Tenant 2",
                connectionString: "Server=...;Database=Tenant2Db;..."));
        });
}
```

#### 方式二：使用配置文件

**appsettings.json**:
```json
{
  "Tenants": [
    {
      "Id": "tenant1",
      "Name": "Tenant 1",
      "ConnectionString": "Server=...;Database=Tenant1Db;..."
    },
    {
      "Id": "tenant2",
      "Name": "Tenant 2",
      "ConnectionString": "Server=...;Database=Tenant2Db;..."
    }
  ],
  "MultiTenancy": {
    "ResolutionStrategy": "Header",
    "TenantHeaderName": "X-Tenant-Id"
  }
}
```

**Program.cs**:
```csharp
services.AddMultiTenancyWithConfiguration(options =>
{
    options.ResolutionStrategy = TenantResolutionStrategy.Header;
});
```

### 2. 添加中间件

```csharp
public void Configure(IApplicationBuilder app)
{
    // 必须在其他中间件之前添加
    app.UseMultiTenancy();

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseEndpoints(endpoints => endpoints.MapControllers());
}
```

### 3. 使用当前租户

```csharp
public class ProductService
{
    private readonly ICurrentTenant _currentTenant;

    public ProductService(ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
    }

    public void GetTenantInfo()
    {
        var tenantId = _currentTenant.Id;
        var tenantName = _currentTenant.Tenant?.Name;
        var connectionString = _currentTenant.Tenant?.ConnectionString;

        Console.WriteLine($"当前租户: {tenantId} - {tenantName}");
    }
}
```

---

## 租户隔离策略

### 1. 数据库隔离（Database）

每个租户使用独立的数据库，数据完全隔离。

```csharp
// 配置
services.AddMultiTenancy(options =>
{
    options.IsolationStrategy = TenantIsolationStrategy.Database;
});

// EF Core DbContext 配置
services.AddDbContext<AppDbContext>((sp, options) =>
{
    var currentTenant = sp.GetRequiredService<ICurrentTenant>();
    var connectionString = currentTenant.Tenant?.ConnectionString 
        ?? "DefaultConnectionString";
    
    options.UseSqlServer(connectionString);
});
```

**优点**：
- ✅ 完全数据隔离
- ✅ 易于备份和恢复单个租户
- ✅ 可为不同租户使用不同数据库类型

**缺点**：
- ❌ 资源消耗较大
- ❌ 管理复杂度高

### 2. 鉴别器隔离（Discriminator）

所有租户共享同一数据库，通过 `TenantId` 字段区分。

#### 定义多租户实体

```csharp
using CrestCreates.Infrastructure.EntityFrameworkCore.MultiTenancy;

public class Product : MultiTenantEntity<Guid>
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    // TenantId 已在基类中定义
}

// 或者实现接口
public class Order : IMultiTenant
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } // 必须
    public string OrderNumber { get; set; }
}
```

#### 配置 DbContext

```csharp
using CrestCreates.Infrastructure.EntityFrameworkCore.MultiTenancy;
using CrestCreates.Infrastructure.EntityFrameworkCore.Interceptors;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentTenant currentTenant) 
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置多租户全局查询过滤器
        modelBuilder.ConfigureTenantDiscriminator(_currentTenant);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // 添加多租户拦截器（自动设置 TenantId）
        optionsBuilder.AddInterceptors(new MultiTenancyInterceptor(_currentTenant));
    }
}
```

#### 服务注册

```csharp
services.AddMultiTenancy(options =>
{
    options.IsolationStrategy = TenantIsolationStrategy.Discriminator;
});

services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer("Server=...;Database=SharedDb;...");
});
```

**自动功能**：
- ✅ 查询时自动过滤租户数据
- ✅ 插入时自动设置 TenantId
- ✅ 防止跨租户修改/删除

**优点**：
- ✅ 资源利用率高
- ✅ 维护成本低
- ✅ 易于管理

**缺点**：
- ❌ 数据隔离不如独立数据库
- ❌ 需要小心查询性能

### 3. Schema 隔离

每个租户使用独立的 Schema。

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    var currentTenant = sp.GetRequiredService<ICurrentTenant>();
    var schema = currentTenant.Id ?? "dbo";
    
    options.UseSqlServer("ConnectionString")
        .UseDefaultSchema(schema);
});
```

---

## 租户识别策略

### 1. HTTP Header（推荐）

```csharp
services.AddMultiTenancy(options =>
{
    options.ResolutionStrategy = TenantResolutionStrategy.Header;
    options.TenantHeaderName = "X-Tenant-Id";
});
```

**客户端请求**：
```http
GET /api/products HTTP/1.1
Host: api.example.com
X-Tenant-Id: tenant1
```

### 2. 子域名

```csharp
services.AddMultiTenancy(options =>
{
    options.ResolutionStrategy = TenantResolutionStrategy.Subdomain;
    options.RootDomain = "example.com";
});
```

**访问方式**：
- `tenant1.example.com` → 租户ID: tenant1
- `tenant2.example.com` → 租户ID: tenant2

### 3. 查询字符串

```csharp
services.AddMultiTenancy(options =>
{
    options.ResolutionStrategy = TenantResolutionStrategy.QueryString;
    options.TenantQueryStringKey = "tenantId";
});
```

**访问方式**：
```
https://api.example.com/products?tenantId=tenant1
```

### 4. 组合策略

```csharp
services.AddMultiTenancy(options =>
{
    // 优先级: Header > Subdomain > QueryString
    options.ResolutionStrategy = 
        TenantResolutionStrategy.Header | 
        TenantResolutionStrategy.Subdomain | 
        TenantResolutionStrategy.QueryString;
});
```

---

## 配置选项

### MultiTenancyOptions 完整配置

```csharp
services.AddMultiTenancy(options =>
{
    // 租户识别策略
    options.ResolutionStrategy = TenantResolutionStrategy.Header;
    
    // 租户隔离策略
    options.IsolationStrategy = TenantIsolationStrategy.Database;
    
    // HTTP Header 名称
    options.TenantHeaderName = "X-Tenant-Id";
    
    // 查询字符串参数名
    options.TenantQueryStringKey = "tenantId";
    
    // Cookie 名称
    options.TenantCookieName = "TenantId";
    
    // 路由参数名
    options.TenantRouteKey = "tenantId";
    
    // 根域名（子域名策略）
    options.RootDomain = "example.com";
    
    // 默认租户ID
    options.DefaultTenantId = "default";
    
    // 是否允许无租户访问
    options.AllowNoTenant = false;
    
    // 租户ID字段名（鉴别器模式）
    options.TenantIdColumnName = "TenantId";
    
    // 启用租户缓存
    options.EnableTenantCache = true;
    
    // 缓存过期时间（分钟）
    options.TenantCacheExpirationMinutes = 60;
});
```

---

## 使用示例

### 示例 1: 简单的多租户 API

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;

    public ProductsController(
        AppDbContext dbContext,
        ICurrentTenant currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts()
    {
        // 自动过滤当前租户的数据
        var products = await _dbContext.Products.ToListAsync();
        
        return Ok(new 
        { 
            TenantId = _currentTenant.Id,
            Products = products 
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateProduct(Product product)
    {
        // TenantId 会自动设置
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();
        
        return CreatedAtAction(nameof(GetProducts), new { id = product.Id }, product);
    }
}
```

### 示例 2: 手动切换租户

```csharp
public class ReportService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly AppDbContext _dbContext;

    public async Task<Report> GenerateCrossTenanttReport()
    {
        var report = new Report();

        // 为 tenant1 生成报告
        using (_currentTenant.Change("tenant1"))
        {
            report.Tenant1Data = await _dbContext.Products.ToListAsync();
        }

        // 为 tenant2 生成报告
        using (_currentTenant.Change("tenant2"))
        {
            report.Tenant2Data = await _dbContext.Products.ToListAsync();
        }

        return report;
    }
}
```

### 示例 3: 自定义租户提供者

```csharp
// 从数据库读取租户信息
public class DatabaseTenantProvider : ITenantProvider
{
    private readonly IDbConnection _connection;
    private readonly ILogger<DatabaseTenantProvider> _logger;

    public DatabaseTenantProvider(
        IDbConnection connection,
        ILogger<DatabaseTenantProvider> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<ITenantInfo> GetTenantAsync(
        string tenantId, 
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id, Name, ConnectionString 
            FROM Tenants 
            WHERE Id = @TenantId AND IsActive = 1";

        var tenant = await _connection.QuerySingleOrDefaultAsync<TenantDto>(
            sql, 
            new { TenantId = tenantId });

        if (tenant == null)
        {
            _logger.LogWarning("Tenant not found: {TenantId}", tenantId);
            return null;
        }

        return new TenantInfo(tenant.Id, tenant.Name, tenant.ConnectionString);
    }
}

// 注册
services.AddTenantProvider<DatabaseTenantProvider>();
```

### 示例 4: 禁用租户过滤器

```csharp
public class AdminService
{
    private readonly AppDbContext _dbContext;

    // 查询所有租户的数据（管理员功能）
    public async Task<List<Product>> GetAllProductsAcrossAllTenants()
    {
        // 临时禁用租户过滤器
        var products = await _dbContext.Products
            .IgnoreQueryFilters()
            .ToListAsync();

        return products;
    }
}
```

---

## 高级用法

### 1. 动态租户注册

```csharp
public class TenantManagementService
{
    private readonly InMemoryTenantProvider _tenantProvider;

    public void RegisterNewTenant(string id, string name, string connectionString)
    {
        var tenant = new TenantInfo(id, name, connectionString);
        _tenantProvider.AddTenant(tenant);
        
        // 可选：创建数据库
        CreateTenantDatabase(connectionString);
    }

    public void RemoveTenant(string tenantId)
    {
        _tenantProvider.RemoveTenant(tenantId);
    }
}
```

### 2. 租户数据迁移

```csharp
public class TenantMigrationService
{
    private readonly ITenantProvider _tenantProvider;

    public async Task MigrateAllTenants()
    {
        var tenants = _tenantProvider.GetAllTenants();

        foreach (var tenant in tenants)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(tenant.ConnectionString);

            using var context = new AppDbContext(optionsBuilder.Options, null);
            await context.Database.MigrateAsync();
            
            Console.WriteLine($"Migrated: {tenant.Name}");
        }
    }
}
```

### 3. 租户特定配置

```csharp
public class TenantConfigurationService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IConfiguration _configuration;

    public string GetTenantSpecificSetting(string key)
    {
        var tenantId = _currentTenant.Id;
        
        // 优先读取租户特定配置
        var tenantKey = $"Tenants:{tenantId}:{key}";
        var value = _configuration[tenantKey];
        
        // 回退到全局配置
        return value ?? _configuration[key];
    }
}
```

---

## 最佳实践

### ✅ 推荐做法

1. **选择合适的隔离策略**
   - 大客户、高安全要求 → Database 隔离
   - 小客户、成本优先 → Discriminator 隔离

2. **使用 Header 识别租户**
   - API 场景最灵活
   - 便于测试和调试

3. **始终验证租户权限**
   ```csharp
   public async Task<Product> GetProduct(Guid productId)
   {
       var product = await _dbContext.Products.FindAsync(productId);
       
       // 额外验证（双重保险）
       if (product?.TenantId != _currentTenant.Id)
       {
           throw new UnauthorizedAccessException("Cross-tenant access denied");
       }
       
       return product;
   }
   ```

4. **为多租户实体添加索引**
   ```csharp
   modelBuilder.Entity<Product>()
       .HasIndex(p => p.TenantId)
       .HasDatabaseName("IX_Product_TenantId");
   ```

5. **记录租户操作日志**
   ```csharp
   _logger.LogInformation(
       "User {UserId} from tenant {TenantId} created product {ProductId}",
       userId, _currentTenant.Id, productId);
   ```

### ❌ 避免做法

1. **不要硬编码租户ID**
   ```csharp
   // ❌ 错误
   var products = _dbContext.Products.Where(p => p.TenantId == "tenant1");
   
   // ✅ 正确
   var products = _dbContext.Products; // 过滤器自动应用
   ```

2. **不要在查询中手动过滤租户**
   ```csharp
   // ❌ 错误（重复且易遗漏）
   var products = _dbContext.Products
       .Where(p => p.TenantId == _currentTenant.Id);
   
   // ✅ 正确（使用全局过滤器）
   var products = _dbContext.Products;
   ```

3. **不要忘记处理无租户场景**
   ```csharp
   if (_currentTenant.Id == null && !_options.AllowNoTenant)
   {
       throw new InvalidOperationException("Tenant is required");
   }
   ```

---

## 故障排除

### 问题 1: 租户未识别

**症状**：`_currentTenant.Id` 为 null

**解决方案**：
1. 检查中间件顺序 - `UseMultiTenancy()` 应在最前面
2. 验证 Header/Subdomain 是否正确传递
3. 检查租户提供者是否正确配置

### 问题 2: 跨租户数据泄露

**症状**：查询到其他租户的数据

**解决方案**：
1. 确认实体实现了 `IMultiTenant`
2. 检查 `ConfigureTenantDiscriminator` 是否调用
3. 验证 `MultiTenancyInterceptor` 是否添加

### 问题 3: 性能问题

**症状**：查询缓慢

**解决方案**：
1. 为 `TenantId` 添加索引
2. 启用租户缓存
3. 考虑使用数据库隔离减少过滤开销

---

## 完整示例项目结构

```
YourProject/
├── Program.cs
├── appsettings.json
├── Controllers/
│   └── ProductsController.cs
├── Services/
│   ├── ProductService.cs
│   └── TenantManagementService.cs
├── Data/
│   ├── AppDbContext.cs
│   └── Migrations/
├── Entities/
│   ├── Product.cs (MultiTenantEntity)
│   └── Order.cs (IMultiTenant)
└── Providers/
    └── DatabaseTenantProvider.cs
```

---

## 参考资源

- [EF Core 全局查询过滤器](https://docs.microsoft.com/ef/core/querying/filters)
- [ASP.NET Core 中间件](https://docs.microsoft.com/aspnet/core/fundamentals/middleware)
- [多租户架构模式](https://docs.microsoft.com/azure/architecture/patterns/saas)

---

**最后更新**: 2025年10月30日  
**版本**: 1.0.0
