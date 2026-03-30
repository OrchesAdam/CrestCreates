# 授权特性 AOP 植入指南

## 📖 概述

本指南介绍如何通过 AOP（面向切面编程）方式将授权特性自动植入到生成的控制器或服务中。

## 🎯 支持的方式

### 1. 代码生成时植入（推荐）✅

**优点**:
- ✅ 编译时生成，性能最佳
- ✅ 代码可见，易于调试
- ✅ 类型安全
- ✅ 支持 IDE 智能提示

**实现方式**:
```csharp
var config = new AuthorizationConfig
{
    ResourceName = "Products",
    GenerateCrudPermissions = true
};

var generator = new AuthorizationAttributeGenerator(config);
var code = generator.GenerateController("Product", "Products");
```

**生成的代码**:
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    [HttpPost]
    [AuthorizePermission("Products.Create")]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
    {
        // ...
    }

    [HttpPut("{id}")]
    [AuthorizePermission("Products.Update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductDto dto)
    {
        // ...
    }
}
```

### 2. Roslyn 语法树注入

**优点**:
- ✅ 可以修改现有代码
- ✅ 精确控制注入位置
- ✅ 支持复杂的条件逻辑

**实现方式**:
```csharp
var existingCode = File.ReadAllText("ProductsController.cs");

var methodPermissions = new Dictionary<string, string>
{
    ["GetAll"] = "Products.View",
    ["Create"] = "Products.Create",
    ["Delete"] = "Products.Delete"
};

var injector = new RoslynAuthorizationInjector();
var modifiedCode = injector.InjectAuthorizationAttributes(existingCode, methodPermissions);

File.WriteAllText("ProductsController.cs", modifiedCode);
```

### 3. 运行时动态代理（未实现，可扩展）

**概念示例**:
```csharp
// 使用 Castle.DynamicProxy 或 Autofac 拦截器
public class AuthorizationInterceptor : IInterceptor
{
    public void Intercept(IInvocation invocation)
    {
        // 在方法执行前检查权限
        if (!HasPermission(invocation.Method))
        {
            throw new UnauthorizedAccessException();
        }

        invocation.Proceed();
    }
}
```

---

## 🔧 配置方式

### 方式 1: 代码配置

```csharp
var config = new AuthorizationConfig
{
    ResourceName = "Products",
    GenerateCrudPermissions = true,
    CustomPermissions = new Dictionary<string, string>
    {
        ["Import"] = "Products.Import",
        ["Export"] = "Products.Export"
    },
    DefaultRoles = new[] { "Admin", "Manager" },
    RequireAll = false // OR 逻辑
};
```

### 方式 2: JSON 配置文件

**authorization-config.json**:
```json
{
  "entities": [
    {
      "entityName": "Product",
      "resourceName": "Products",
      "generateCrudPermissions": true,
      "requiredRoles": ["Admin", "ProductManager"],
      "customPermissions": {
        "Import": "Products.Import",
        "Export": "Products.Export",
        "BulkUpdate": "Products.BulkUpdate"
      }
    },
    {
      "entityName": "Order",
      "resourceName": "Orders",
      "generateCrudPermissions": true,
      "requiredRoles": ["Admin", "OrderManager"],
      "customPermissions": {
        "Approve": "Orders.Approve",
        "Cancel": "Orders.Cancel",
        "Ship": "Orders.Ship"
      }
    }
  ]
}
```

**加载配置**:
```csharp
var json = File.ReadAllText("authorization-config.json");
var configData = JsonSerializer.Deserialize<ConfigFile>(json);

var batchGenerator = new BatchAuthorizationGenerator();
var results = batchGenerator.GenerateControllers(configData.Entities);
```

### 方式 3: 特性配置（未来支持）

```csharp
[GenerateController]
[AuthorizationConfig(
    ResourceName = "Products",
    RequiredRoles = new[] { "Admin" },
    GenerateCrudPermissions = true)]
public class Product : Entity<Guid>
{
    public string Name { get; set; }
    
    [CustomPermission("Products.Import")]
    public void ImportFromExcel() { }
}
```

---

## 📝 使用示例

### 示例 1: 简单 CRUD 控制器

```csharp
public class Program
{
    public static void Main()
    {
        var config = new AuthorizationConfig
        {
            ResourceName = "Products",
            GenerateCrudPermissions = true
        };

        var generator = new AuthorizationAttributeGenerator(config);
        var code = generator.GenerateController("Product", "Products");

        File.WriteAllText("ProductsController.cs", code);
    }
}
```

**生成结果**:
- `GetAll` → `[AuthorizePermission("Products.View")]`
- `GetById` → `[AuthorizePermission("Products.View")]`
- `Create` → `[AuthorizePermission("Products.Create")]`
- `Update` → `[AuthorizePermission("Products.Update")]`
- `Delete` → `[AuthorizePermission("Products.Delete")]`

### 示例 2: 自定义权限

```csharp
var config = new AuthorizationConfig
{
    ResourceName = "Orders",
    GenerateCrudPermissions = true,
    CustomPermissions = new Dictionary<string, string>
    {
        ["Approve"] = "Orders.Approve",
        ["Reject"] = "Orders.Reject",
        ["Ship"] = "Orders.Ship",
        ["GenerateInvoice"] = "Orders.Invoice"
    }
};
```

### 示例 3: 批量生成

```csharp
var entities = new[] { "Product", "Order", "Customer", "Invoice" };

foreach (var entity in entities)
{
    var config = new AuthorizationConfig
    {
        ResourceName = $"{entity}s",
        GenerateCrudPermissions = true,
        DefaultRoles = new[] { "Admin" }
    };

    var generator = new AuthorizationAttributeGenerator(config);
    var code = generator.GenerateController(entity, $"{entity}s");
    
    File.WriteAllText($"{entity}sController.cs", code);
}
```

### 示例 4: 集成到 MSBuild

**CrestCreates.CodeGenerator.targets**:
```xml
<Project>
  <Target Name="GenerateAuthorizationControllers" BeforeTargets="BeforeBuild">
    <Exec Command="dotnet run --project $(SolutionDir)tools\CrestCreates.CodeGenerator -- generate-controllers --config $(ProjectDir)authorization-config.json --output $(ProjectDir)Controllers\Generated" />
  </Target>
</Project>
```

---

## 🎨 高级用法

### 1. 条件生成

```csharp
public string GenerateAuthorizationAttribute(string methodName, string httpMethod)
{
    // 只为敏感操作添加授权
    if (IsSensitiveOperation(methodName))
    {
        return GeneratePermissionAttribute($"{_config.ResourceName}.{methodName}");
    }
    
    return string.Empty;
}

private bool IsSensitiveOperation(string methodName)
{
    return methodName is "Delete" or "Update" or "Create";
}
```

### 2. 权限继承

```csharp
var config = new AuthorizationConfig
{
    ResourceName = "Products",
    CustomPermissions = new Dictionary<string, string>
    {
        // 父权限
        ["ManageProducts"] = "Products.Manage",
        
        // 子权限（继承父权限）
        ["Create"] = "Products.Manage.Create",
        ["Update"] = "Products.Manage.Update",
        ["Delete"] = "Products.Manage.Delete"
    }
};
```

### 3. 多租户支持

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    [HttpGet]
    [AuthorizePermission("Products.View")]
    [RequireTenant] // 自定义特性，要求租户上下文
    public async Task<IActionResult> GetAll()
    {
        // 自动过滤当前租户的数据
    }
}
```

---

## 🚀 与 Source Generator 集成

### Source Generator 方式（C# 9.0+）

```csharp
[Generator]
public class AuthorizationGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        // 1. 查找所有标记的实体
        var entities = context.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes())
            .OfType<ClassDeclarationSyntax>()
            .Where(c => c.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString() == "GenerateController"));

        // 2. 为每个实体生成控制器
        foreach (var entity in entities)
        {
            var entityName = entity.Identifier.Text;
            var code = GenerateControllerCode(entityName);
            
            context.AddSource($"{entityName}Controller.g.cs", code);
        }
    }
}
```

---

## 📊 权限映射规则

### HTTP 方法 → CRUD 权限

| HTTP 方法 | 默认权限 | 说明 |
|-----------|---------|------|
| `GET` | `View` | 查看资源 |
| `POST` | `Create` | 创建资源 |
| `PUT` | `Update` | 更新资源 |
| `PATCH` | `Update` | 部分更新 |
| `DELETE` | `Delete` | 删除资源 |

### 自定义方法命名约定

| 方法名 | 推荐权限 |
|--------|---------|
| `Approve` | `{Resource}.Approve` |
| `Reject` | `{Resource}.Reject` |
| `Export` | `{Resource}.Export` |
| `Import` | `{Resource}.Import` |
| `Publish` | `{Resource}.Publish` |
| `Archive` | `{Resource}.Archive` |

---

## ⚙️ 最佳实践

### ✅ 推荐做法

1. **使用代码生成而非运行时反射**
   ```csharp
   // ✅ 推荐：编译时生成
   var generator = new AuthorizationAttributeGenerator(config);
   var code = generator.GenerateController("Product", "Products");
   
   // ❌ 避免：运行时动态代理（性能开销）
   ```

2. **统一的命名约定**
   ```csharp
   // ✅ 好的命名
   {ResourceName}.Create
   {ResourceName}.Update
   {ResourceName}.Delete
   
   // ❌ 不一致的命名
   Create{ResourceName}
   {ResourceName}Update
   ```

3. **使用配置文件管理权限**
   ```json
   {
     "entities": [
       { "entityName": "Product", "resourceName": "Products" }
     ]
   }
   ```

4. **集成到构建流程**
   ```xml
   <Target Name="GenerateControllers" BeforeTargets="BeforeBuild">
     <!-- 自动生成代码 -->
   </Target>
   ```

### ❌ 避免做法

1. **不要硬编码权限字符串**
2. **不要在运行时生成代码**
3. **不要混合多种生成方式**
4. **不要忽略生成代码的版本控制**

---

## 🔗 集成示例

### 完整的代码生成流程

```bash
# 1. 定义实体
# Entities/Product.cs

# 2. 创建配置文件
# authorization-config.json

# 3. 运行生成器
dotnet run --project tools/CrestCreates.CodeGenerator generate-controllers

# 4. 生成的文件
# Controllers/Generated/ProductsController.cs
# Controllers/Generated/OrdersController.cs

# 5. 编译项目
dotnet build
```

---

## 📚 参考资源

- [Roslyn Source Generators](https://docs.microsoft.com/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [Castle DynamicProxy](http://www.castleproject.org/projects/dynamicproxy/)
- [ASP.NET Core Authorization](https://docs.microsoft.com/aspnet/core/security/authorization)

---

**最后更新**: 2025年10月30日  
**版本**: 1.0.0
