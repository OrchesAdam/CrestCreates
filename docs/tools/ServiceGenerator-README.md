# CrestCreates ServiceGenerator

## 概述

CrestCreates ServiceGenerator 是一个基于 Roslyn Source Generator 的服务层代码生成器，用于自动生成服务注册、API控制器、服务接口和测试基类等基础设施代码。

## 主要特性

### 1. 自动代码生成
- **服务注册**：自动生成 DI 容器注册扩展方法
- **服务接口**：为服务类自动生成接口（如果不存在）
- **API控制器**：生成完整的 RESTful API 控制器
- **服务扩展**：生成便利的服务扩展方法
- **测试基类**：生成 xUnit + Moq 测试基类

### 2. 生命周期管理
支持三种服务生命周期：
- **Scoped**：作用域生命周期（默认）
- **Singleton**：单例生命周期
- **Transient**：瞬态生命周期

### 3. RESTful API 支持
- 自动生成符合 RESTful 规范的路由
- 智能 HTTP 动词映射（GET/POST/PUT/DELETE/PATCH）
- 完整的错误处理和日志记录
- 支持请求参数绑定（FromBody/FromRoute/FromQuery）

### 4. 测试友好
- 生成 Moq 测试基类
- 内置 Mock 设置和验证方法
- 支持测试隔离

## 使用方式

### 1. 服务定义

```csharp
[Service(
    Lifetime = ServiceAttribute.ServiceLifetime.Scoped,
    GenerateController = true,
    Route = "api/products"
)]
public class ProductService : IProductService
{
    public async Task<ProductDto> GetByIdAsync(Guid id)
    {
        // 业务逻辑
    }
    
    public async Task<List<ProductDto>> GetAllAsync()
    {
        // 业务逻辑
    }
    
    public async Task<ProductDto> CreateAsync(CreateProductDto dto)
    {
        // 业务逻辑
    }
}
```

### 2. 生成的代码结构

```
Generated/
├── DependencyInjection/
│   └── AutoServiceRegistration.g.cs      # 服务注册
├── Interfaces/
│   └── IProductService.g.cs              # 服务接口（如需要）
├── Controllers/
│   └── ProductController.g.cs            # API控制器
├── Extensions/
│   └── ProductServiceExtensions.g.cs     # 服务扩展
└── Tests/
    └── ProductServiceTestBase.g.cs        # 测试基类
```

## 生成的代码示例

### 1. 自动服务注册

```csharp
public static class AutoServiceRegistration
{
    public static IServiceCollection AddGeneratedServices(this IServiceCollection services)
    {
        // ProductService - Scoped
        services.AddScoped<IProductService, ProductService>();
        
        // OrderService - Singleton
        services.AddSingleton<IOrderService, OrderService>();
        
        return services;
    }
}
```

**使用方式：**
```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGeneratedServices(); // 一行代码注册所有服务
    }
}
```

### 2. API 控制器

```csharp
[ApiController]
[Route("api/products")]
public partial class ProductController : ControllerBase
{
    private readonly IProductService _service;
    private readonly ILogger<ProductController> _logger;
    
    public ProductController(
        IProductService service,
        ILogger<ProductController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// GetByIdAsync
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetByIdAsync([FromRoute] Guid id)
    {
        try
        {
            var result = await _service.GetByIdAsync(id);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Bad request in GetByIdAsync");
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetByIdAsync");
            return StatusCode(500, "An error occurred while processing your request.");
        }
    }
    
    [HttpGet("")]
    public async Task<IActionResult> GetAllAsync()
    {
        // ...
    }
    
    [HttpPost("")]
    public async Task<IActionResult> CreateAsync([FromBody] CreateProductDto dto)
    {
        // ...
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(
        [FromRoute] Guid id, 
        [FromBody] UpdateProductDto dto)
    {
        // ...
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync([FromRoute] Guid id)
    {
        // ...
    }
}
```

### 3. 服务扩展方法

```csharp
public static class ProductServiceExtensions
{
    /// <summary>
    /// 安全执行服务方法，捕获异常
    /// </summary>
    public static async Task<(bool Success, T? Result, string? Error)> TryExecuteAsync<T>(
        this IProductService service,
        Func<IProductService, Task<T>> action)
    {
        try
        {
            var result = await action(service);
            return (true, result, null);
        }
        catch (Exception ex)
        {
            return (false, default, ex.Message);
        }
    }
}
```

**使用方式：**
```csharp
var (success, product, error) = await _productService.TryExecuteAsync(
    s => s.GetByIdAsync(productId));

if (success)
{
    Console.WriteLine($"Product: {product.Name}");
}
else
{
    Console.WriteLine($"Error: {error}");
}
```

### 4. 测试基类

```csharp
public abstract class ProductServiceTestBase
{
    protected Mock<IProductService> ServiceMock { get; }
    protected IProductService Service => ServiceMock.Object;
    
    protected ProductServiceTestBase()
    {
        ServiceMock = new Mock<IProductService>();
        SetupMocks();
    }
    
    /// <summary>
    /// 设置Mock对象的行为
    /// </summary>
    protected virtual void SetupMocks()
    {
        // 在派生类中重写此方法以设置特定的Mock行为
    }
    
    /// <summary>
    /// 验证Mock方法被调用
    /// </summary>
    protected void VerifyServiceCalled<TResult>(
        Func<IProductService, TResult> expression,
        Times? times = null)
    {
        ServiceMock.Verify(expression, times ?? Times.Once());
    }
}
```

**使用方式：**
```csharp
public class ProductServiceTests : ProductServiceTestBase
{
    protected override void SetupMocks()
    {
        ServiceMock
            .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new ProductDto { Id = Guid.NewGuid(), Name = "Test" });
    }
    
    [Fact]
    public async Task GetByIdAsync_ShouldReturnProduct()
    {
        // Arrange
        var productId = Guid.NewGuid();
        
        // Act
        var result = await Service.GetByIdAsync(productId);
        
        // Assert
        Assert.NotNull(result);
        VerifyServiceCalled(s => s.GetByIdAsync(productId));
    }
}
```

## 配置选项

### ServiceAttribute 参数

- **Lifetime**：服务生命周期（Scoped/Singleton/Transient，默认：Scoped）
- **GenerateController**：是否生成API控制器（默认：true）
- **Route**：自定义API路由（默认：api/{servicename}）

## HTTP 动词映射规则

### GET 请求
- `GetByIdAsync(Guid id)` → `GET api/products/{id}`
- `GetAllAsync()` → `GET api/products`
- `GetByCategoryAsync(string category)` → `GET api/products/bycategory/{category}`

### POST 请求
- `CreateAsync(CreateDto dto)` → `POST api/products`
- `AddAsync(CreateDto dto)` → `POST api/products`

### PUT 请求
- `UpdateAsync(Guid id, UpdateDto dto)` → `PUT api/products/{id}`

### DELETE 请求
- `DeleteAsync(Guid id)` → `DELETE api/products/{id}`
- `RemoveAsync(Guid id)` → `DELETE api/products/{id}`

## 参数绑定规则

### FromRoute
- 简单类型的第一个参数（在 GET/PUT/DELETE 请求中）
- Guid、int、string 等基本类型

### FromBody
- DTO 类型（名称包含 "Dto"）
- 复杂对象类型（在 POST/PUT 请求中）

### FromQuery
- GET 请求中的复杂类型参数

## 最佳实践

### 1. 服务设计
```csharp
// ✅ 好的实践
[Service(Lifetime = ServiceLifetime.Scoped)]
public class ProductService : IProductService
{
    private readonly IProductRepository _repository;
    private readonly IMapper _mapper;
    
    public ProductService(
        IProductRepository repository,
        IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }
    
    public async Task<ProductDto> GetByIdAsync(Guid id)
    {
        var product = await _repository.GetByIdAsync(id);
        return _mapper.Map<ProductDto>(product);
    }
}

// ❌ 避免
public class ProductService // 缺少 [Service] 特性
{
    // 直接依赖具体类而非接口
    public ProductService(ProductRepository repository) { }
}
```

### 2. 控制器扩展
```csharp
// 生成的控制器是 partial 的，可以扩展
public partial class ProductController
{
    // 添加自定义端点
    [HttpGet("featured")]
    public async Task<IActionResult> GetFeaturedProducts()
    {
        var products = await _service.GetFeaturedProductsAsync();
        return Ok(products);
    }
}
```

### 3. 错误处理
```csharp
public class ProductService : IProductService
{
    public async Task<ProductDto> CreateAsync(CreateProductDto dto)
    {
        // 抛出特定异常，控制器会自动处理
        if (string.IsNullOrEmpty(dto.Name))
            throw new ArgumentException("Product name is required");
        
        // 业务逻辑
    }
}
```

## 架构优势

### 1. 约定优于配置
- 智能路由生成
- 自动 HTTP 动词映射
- 默认的生命周期管理

### 2. 关注点分离
- 服务层专注业务逻辑
- 控制器自动生成
- 测试代码独立

### 3. DI 容器集成
- 编译时注册
- 类型安全
- 避免运行时错误

### 4. API 规范化
- 统一的路由格式
- 一致的错误处理
- 标准化的响应格式

## 性能考虑

- **编译时生成**：所有代码在编译时生成，无运行时开销
- **无反射**：服务注册不使用反射，提高启动速度
- **AOT 友好**：完全支持 .NET Native AOT 编译

## 扩展点

### 1. Partial 类扩展
```csharp
// 生成的代码
public partial class ProductController : ControllerBase
{
    // 自动生成的方法
}

// 你的代码
public partial class ProductController
{
    // 添加自定义方法
    [HttpGet("search")]
    public async Task<IActionResult> Search(string keyword)
    {
        // 自定义逻辑
    }
}
```

### 2. 自定义中间件
```csharp
public class Startup
{
    public void Configure(IApplicationBuilder app)
    {
        app.UseMiddleware<CustomAuthenticationMiddleware>();
        // ...
    }
}
```

## 故障排除

### 常见问题

1. **服务未注册**
   - 确保服务类标记了 `[Service]` 特性
   - 检查是否调用了 `AddGeneratedServices()`

2. **控制器未生成**
   - 检查 `GenerateController` 属性是否为 true
   - 确保服务有对应的接口

3. **路由冲突**
   - 使用 `Route` 属性自定义路由
   - 避免方法名称冲突

### 调试技巧

- 查看生成的文件：在 `obj/Debug/net10.0/generated/` 目录下
- 开启详细构建日志：`dotnet build -v detailed`
- 检查诊断信息：查找 CCCG002 错误代码

## 与其他生成器的集成

ServiceGenerator 与 EntityGenerator 无缝集成：

```csharp
[Service]
public class ProductService : IProductService
{
    private readonly IProductRepository _repository; // 由 EntityGenerator 生成
    
    public ProductService(IProductRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<ProductDto> GetByIdAsync(Guid id)
    {
        var product = await _repository.GetByIdAsync(id);
        return _mapper.Map<ProductDto>(product);
    }
}
```

## 总结

ServiceGenerator 提供了完整的服务层代码生成解决方案，帮助开发者：

- ✅ 减少样板代码
- ✅ 统一 API 规范
- ✅ 简化服务注册
- ✅ 提高测试效率
- ✅ 保持代码一致性

这个实现完全符合 CrestCreates 框架的设计理念，为开发者提供了高效、类型安全、易于维护的服务层开发体验。