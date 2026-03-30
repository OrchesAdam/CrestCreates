# ServiceGenerator 快速参考

## 目录结构

```
test/CrestCreates.CodeGenerator.Tests/
└── Services/
    ├── ProductService.cs      # 有接口的服务（完整示例）
    ├── OrderService.cs        # 无接口的服务（接口自动生成）
    ├── README.md              # 详细使用文档
    └── 迁移总结.md            # 迁移说明
```

## 快速对比

| 特性 | ProductService | OrderService |
|------|---------------|--------------|
| **接口** | ✅ 手动定义 `IProductService` | ❌ 自动生成 `IOrderService` |
| **方法数** | 6 个 | 3 个 |
| **返回类型** | Task<DTO> | Task<DTO>, Task<bool> |
| **API 端点** | 6 个 | 3 个 |
| **测试目标** | 完整功能 | 接口生成 + bool 处理 |

## 使用示例

### 1. 定义服务

```csharp
[Service(
    Lifetime = ServiceLifetime.Scoped,  // Scoped/Singleton/Transient
    GenerateController = true,           // 生成 API 控制器
    Route = "api/products"               // API 路由前缀
)]
public class ProductService : IProductService
{
    public async Task<ProductDto> GetByIdAsync(Guid id) { ... }
}
```

### 2. 自动生成

编译后自动生成：
- `AutoServiceRegistration.g.cs` - DI 注册
- `ProductController.g.cs` - RESTful API
- `ProductServiceExtensions.g.cs` - 扩展方法
- `ProductServiceTestBase.g.cs` - 测试基类

### 3. 使用生成的代码

```csharp
// Startup.cs
services.AddGeneratedServices();

// 测试
public class ProductServiceTests : ProductServiceTestBase
{
    [Fact]
    public async Task Test() { ... }
}
```

## HTTP 动词映射规则

| 方法名模式 | HTTP 动词 | 路由模板 | 参数绑定 |
|-----------|----------|----------|---------|
| `GetByIdAsync(id)` | GET | `/{id}` | FromRoute |
| `GetAllAsync()` | GET | `/` | - |
| `GetByCategoryAsync(category)` | GET | `?category={category}` | FromQuery |
| `CreateAsync(dto)` | POST | `/` | FromBody |
| `UpdateAsync(id, dto)` | PUT | `/{id}` | FromRoute + FromBody |
| `DeleteAsync(id)` | DELETE | `/{id}` | FromRoute |

## 快速测试

```bash
# 1. 编译
dotnet build test/CrestCreates.CodeGenerator.Tests

# 2. 查看生成文件
cd test/CrestCreates.CodeGenerator.Tests/Generated
dir /s *.g.cs

# 3. 检查生成的内容
# 应该看到：
# - AutoServiceRegistration.g.cs
# - ProductController.g.cs
# - OrderController.g.cs
# - IOrderService.g.cs ⭐
# - 各种 Extensions 和 TestBase
```

## 关键点

✅ **有接口**：使用 ProductService 示例  
✅ **无接口**：使用 OrderService 示例（会自动生成接口）  
✅ **API 自动化**：控制器和路由自动生成  
✅ **测试友好**：自动生成 Mock 测试基类  
✅ **类型安全**：编译时检查，无运行时反射  
