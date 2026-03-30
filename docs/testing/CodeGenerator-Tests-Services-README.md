# ServiceGenerator 测试示例

本目录包含用于测试 ServiceGenerator 功能的示例服务。

## 示例服务

### 1. ProductService.cs
**测试场景**：带接口的服务
- ✅ 显式定义了 `IProductService` 接口
- ✅ 测试服务注册生成
- ✅ 测试 API 控制器生成
- ✅ 测试服务扩展方法生成
- ✅ 测试测试基类生成

**生成的代码**：
- `AutoServiceRegistration.g.cs` - 服务注册扩展
- `ProductController.g.cs` - RESTful API 控制器
- `ProductServiceExtensions.g.cs` - 服务扩展方法
- `ProductServiceTestBase.g.cs` - xUnit + Moq 测试基类

**API 端点**：
```
GET    /api/products/{id}           → GetByIdAsync(id)
GET    /api/products                → GetAllAsync()
POST   /api/products                → CreateAsync(dto)
PUT    /api/products/{id}           → UpdateAsync(id, dto)
DELETE /api/products/{id}           → DeleteAsync(id)
GET    /api/products?category=xxx   → GetByCategoryAsync(category)
```

### 2. OrderService.cs
**测试场景**：无接口的服务
- ✅ **没有**显式定义接口
- ✅ 测试自动生成 `IOrderService` 接口
- ✅ 测试 bool 返回值的处理
- ✅ 测试不同的方法签名

**生成的代码**：
- `IOrderService.g.cs` - **自动生成的接口**
- `AutoServiceRegistration.g.cs` - 服务注册扩展
- `OrderController.g.cs` - RESTful API 控制器
- `OrderServiceExtensions.g.cs` - 服务扩展方法
- `OrderServiceTestBase.g.cs` - 测试基类

**API 端点**：
```
GET    /api/orders/{id}    → GetByIdAsync(id)
POST   /api/orders         → CreateAsync(dto)
DELETE /api/orders/{id}    → CancelAsync(id)  // 注意：bool 返回值
```

## 如何测试

### 1. 编译项目
```bash
dotnet build test/CrestCreates.CodeGenerator.Tests
```

### 2. 查看生成的代码
生成的代码位于：
```
test/CrestCreates.CodeGenerator.Tests/obj/Debug/netX.X/generated/CrestCreates.CodeGenerator.ServiceSourceGenerator/
```

### 3. 验证功能

#### 验证服务注册
```csharp
// 生成的代码应该包含：
public static class AutoServiceRegistration
{
    public static IServiceCollection AddGeneratedServices(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>(); // 自动生成的接口
        return services;
    }
}
```

#### 验证接口生成（OrderService）
```csharp
// 应该自动生成：
public partial interface IOrderService
{
    Task<OrderDto> GetByIdAsync(Guid id);
    Task<OrderDto> CreateAsync(CreateOrderDto dto);
    Task<bool> CancelAsync(Guid id);
}
```

#### 验证控制器生成
```csharp
[ApiController]
[Route("api/products")]
public partial class ProductController : ControllerBase
{
    private readonly IProductService _service;
    private readonly ILogger<ProductController> _logger;
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync([FromRoute] Guid id) { ... }
    
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateProductDto dto) { ... }
}
```

#### 验证测试基类生成
```csharp
public abstract class ProductServiceTestBase
{
    protected Mock<IProductService> ServiceMock { get; }
    protected IProductService Service => ServiceMock.Object;
    
    protected virtual void SetupMocks() { }
}
```

## 预期生成文件清单

### ProductService
- ✅ `ProductController.g.cs`
- ✅ `ProductServiceExtensions.g.cs`
- ✅ `ProductServiceTestBase.g.cs`
- ✅ 贡献到 `AutoServiceRegistration.g.cs`

### OrderService
- ✅ `IOrderService.g.cs` ⭐ **自动生成接口**
- ✅ `OrderController.g.cs`
- ✅ `OrderServiceExtensions.g.cs`
- ✅ `OrderServiceTestBase.g.cs`
- ✅ 贡献到 `AutoServiceRegistration.g.cs`

## 注意事项

1. **命名空间**：所有测试服务使用 `CrestCreates.CodeGenerator.Tests.Services` 命名空间
2. **依赖注入**：默认使用 `Scoped` 生命周期
3. **路由规则**：遵循 RESTful 规范，自动映射 HTTP 动词
4. **参数绑定**：
   - ID 参数 → `[FromRoute]`
   - DTO 对象 → `[FromBody]`
   - 简单查询参数 → `[FromQuery]`

## 调试技巧

如果生成器没有工作：

1. **清理并重新编译**：
   ```bash
   dotnet clean
   dotnet build
   ```

2. **查看诊断输出**：
   检查 Visual Studio 的输出窗口中的"生成"选项卡

3. **验证特性**：
   确保 `[Service]` 特性正确应用

4. **检查生成文件**：
   在 `obj/Debug/netX.X/generated/` 目录下查找生成的文件
