# RBAC 授权体系使用指南

## 📖 目录

1. [概述](#概述)
2. [快速开始](#快速开始)
3. [权限定义](#权限定义)
4. [权限检查](#权限检查)
5. [角色管理](#角色管理)
6. [授权特性](#授权特性)
7. [高级用法](#高级用法)
8. [最佳实践](#最佳实践)

---

## 概述

CrestCreates 框架提供了完整的 RBAC (Role-Based Access Control) 授权体系，包括：

### ✨ 核心功能

- **权限定义系统**
  - 层级权限结构
  - 权限组管理
  - 自定义权限属性
  - 批量 CRUD 权限生成

- **权限检查**
  - 单权限/多权限检查
  - AND/OR 逻辑组合
  - 基于用户和角色的权限
  - 权限缓存支持

- **角色管理**
  - 角色CRUD操作
  - 用户-角色关联
  - 默认角色支持
  - 系统内置角色保护

- **授权特性**
  - `[AuthorizePermission]` 特性
  - `[AuthorizeRoles]` 特性
  - 策略授权支持
  - 自定义授权处理器

---

## 快速开始

### 1. 注册服务

#### 方式一：完整配置（推荐）

```csharp
// Program.cs
services.AddCompleteRbac(
    options =>
    {
        options.EnablePermissionCache = true;
        options.PermissionCacheExpirationMinutes = 20;
        options.AutoAssignDefaultRoles = true;
    },
    enableCache: true);

// 添加权限定义
services.AddPermissionDefinitionProvider<MyPermissionDefinitionProvider>();
```

#### 方式二：手动配置

```csharp
services.AddRbacAuthorization();
services.AddInMemoryPermissionStore(enableCache: true);
services.AddRoleManagement();
services.AddPermissionDefinitionProvider<MyPermissionDefinitionProvider>();
```

### 2. 定义权限

```csharp
public class MyPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var productGroup = context.AddGroup("Products", "产品管理");
        
        // 手动定义权限
        productGroup.AddPermission(
            "Products.Create",
            "创建产品",
            "允许创建新产品");

        productGroup.AddPermission(
            "Products.Update",
            "更新产品");

        // 或使用扩展方法批量生成 CRUD 权限
        productGroup.AddCrudPermissions("Orders", "订单");
        // 生成: Orders.Create, Orders.Update, Orders.Delete, Orders.View
    }
}
```

### 3. 配置权限

```csharp
// 为角色授予权限
var permissionStore = serviceProvider.GetRequiredService<InMemoryPermissionStore>();

permissionStore.GrantToRole("Admin", 
    "Products.Create",
    "Products.Update",
    "Products.Delete",
    "Products.View");

permissionStore.GrantToRole("User", 
    "Products.View");

// 为用户直接授予权限
permissionStore.GrantToUser("user-123", "Products.Create");
```

### 4. 使用授权特性

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    // 需要单个权限
    [HttpPost]
    [AuthorizePermission("Products.Create")]
    public IActionResult CreateProduct([FromBody] ProductDto product)
    {
        // ...
    }

    // 需要任一权限
    [HttpGet]
    [AuthorizePermission("Products.View", "Products.Manage")]
    public IActionResult GetProducts()
    {
        // ...
    }

    // 需要所有权限
    [HttpPut("{id}")]
    [AuthorizePermission("Products.Update", "Products.Manage", RequireAll = true)]
    public IActionResult UpdateProduct(int id, [FromBody] ProductDto product)
    {
        // ...
    }
}
```

---

## 权限定义

### 层级权限结构

```csharp
public void Define(IPermissionDefinitionContext context)
{
    var group = context.AddGroup("Admin", "管理员");

    // 父权限
    var systemManagement = group.AddPermission(
        "Admin.System",
        "系统管理",
        "所有系统管理权限");

    // 子权限
    systemManagement.AddChild(
        "Admin.System.Users",
        "用户管理");

    systemManagement.AddChild(
        "Admin.System.Roles",
        "角色管理");

    // 孙权限
    var userManagement = systemManagement.Children
        .First(p => p.Name == "Admin.System.Users");
    
    userManagement.AddChild(
        "Admin.System.Users.Create",
        "创建用户");
}
```

### 权限组

```csharp
public void Define(IPermissionDefinitionContext context)
{
    // 产品组
    var productGroup = context.AddGroup("Products", "产品管理");
    productGroup.AddCrudPermissions("Products", "产品");

    // 订单组
    var orderGroup = context.AddGroup("Orders", "订单管理");
    orderGroup.AddCrudPermissions("Orders", "订单");

    // 用户组
    var userGroup = context.AddGroup("Users", "用户管理");
    userGroup.AddPermission("Users.View", "查看用户");
    userGroup.AddPermission("Users.Edit", "编辑用户");
}
```

### 自定义属性

```csharp
var permission = group.AddPermission("Products.Delete", "删除产品");

// 添加自定义属性
permission.WithProperty("RequireReason", true)
          .WithProperty("AuditLevel", "High")
          .WithProperty("Category", "Sensitive");

// 获取属性
var requireReason = permission.Properties["RequireReason"];
```

---

## 权限检查

### 在服务中检查权限

```csharp
public class ProductService
{
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICurrentUser _currentUser;

    public ProductService(
        IPermissionChecker permissionChecker,
        ICurrentUser currentUser)
    {
        _permissionChecker = permissionChecker;
        _currentUser = currentUser;
    }

    public async Task<bool> CanCreateProduct()
    {
        // 检查当前用户权限
        return await _permissionChecker.IsGrantedAsync("Products.Create");
    }

    public async Task<Product> CreateProduct(ProductDto dto)
    {
        // 手动权限检查
        if (!await _permissionChecker.IsGrantedAsync("Products.Create"))
        {
            throw new UnauthorizedAccessException("没有创建产品的权限");
        }

        // 业务逻辑...
    }

    public async Task<Dictionary<string, bool>> GetUserPermissions()
    {
        // 批量检查权限
        var result = await _permissionChecker.IsGrantedAsync(new[]
        {
            "Products.Create",
            "Products.Update",
            "Products.Delete"
        });

        return result.Result;
    }
}
```

### 检查特定用户权限

```csharp
public class AdminService
{
    private readonly IPermissionChecker _permissionChecker;

    public async Task<bool> UserHasPermission(string userId, string permission)
    {
        // 构建用户 Principal
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "username")
        };
        var identity = new ClaimsIdentity(claims, "Custom");
        var principal = new ClaimsPrincipal(identity);

        // 检查指定用户的权限
        return await _permissionChecker.IsGrantedAsync(principal, permission);
    }
}
```

---

## 角色管理

### 创建和管理角色

```csharp
public class RoleSetupService
{
    private readonly IRoleManager _roleManager;
    private readonly IPermissionGrantService _permissionGrantService;

    public async Task SetupRoles()
    {
        // 创建角色
        var adminRole = await _roleManager.CreateAsync(
            name: "Admin",
            displayName: "管理员",
            description: "系统管理员角色");

        var userRole = await _roleManager.CreateAsync(
            name: "User",
            displayName: "普通用户");

        // 为角色授予权限
        await _permissionGrantService.GrantAsync(
            "Products.Create", "Role", "Admin");
        await _permissionGrantService.GrantAsync(
            "Products.View", "Role", "User");

        // 更新角色
        await _roleManager.UpdateAsync(
            adminRole.Id,
            displayName: "超级管理员",
            description: "拥有所有权限");

        // 删除角色
        // await _roleManager.DeleteAsync(userRole.Id);
    }
}
```

### 用户角色管理

```csharp
public class UserService
{
    private readonly IUserRoleManager _userRoleManager;

    public async Task ManageUserRoles(string userId)
    {
        // 添加单个角色
        await _userRoleManager.AddToRoleAsync(userId, "Admin");

        // 添加多个角色
        await _userRoleManager.AddToRolesAsync(userId, "Admin", "PowerUser");

        // 获取用户角色
        var roles = await _userRoleManager.GetRolesAsync(userId);
        var roleNames = await _userRoleManager.GetRoleNamesAsync(userId);

        // 检查用户是否在角色中
        var isAdmin = await _userRoleManager.IsInRoleAsync(userId, "Admin");

        // 移除角色
        await _userRoleManager.RemoveFromRoleAsync(userId, "PowerUser");

        // 设置用户角色（替换现有）
        await _userRoleManager.SetRolesAsync(userId, "User", "Customer");
    }
}
```

---

## 授权特性

### AuthorizePermission 特性

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    // 单个权限
    [HttpPost]
    [AuthorizePermission("Products.Create")]
    public IActionResult Create([FromBody] ProductDto product) { }

    // 多个权限（OR 逻辑）
    [HttpGet]
    [AuthorizePermission("Products.View", "Products.Manage")]
    public IActionResult GetAll() { }

    // 多个权限（AND 逻辑）
    [HttpDelete("{id}")]
    [AuthorizePermission("Products.Delete", "Products.ManageAll", RequireAll = true)]
    public IActionResult Delete(int id) { }

    // 组合使用
    [HttpPut("{id}")]
    [AuthorizePermission("Products.Update")]
    [AuthorizeRoles("Admin", "Manager")]
    public IActionResult Update(int id, [FromBody] ProductDto product) { }
}
```

### 策略授权

```csharp
// 配置策略
services.AddPermissionPolicies(options =>
{
    options.AddPermissionPolicy("CanManageProducts", 
        "Products.Create", 
        "Products.Update", 
        "Products.Delete");

    options.AddAllPermissionsPolicy("CanFullyManageProducts", 
        "Products.Create", 
        "Products.Update", 
        "Products.Delete", 
        "Products.View");
});

// 使用策略
[Authorize(Policy = "CanManageProducts")]
public IActionResult ManageProducts() { }
```

---

## 高级用法

### 示例 1: 动态权限授予

```csharp
public class DynamicPermissionService
{
    private readonly IPermissionGrantService _permissionGrantService;

    public async Task GrantPermissionsBasedOnSubscription(
        string userId, 
        string subscriptionTier)
    {
        switch (subscriptionTier)
        {
            case "Premium":
                await _permissionGrantService.GrantAsync("Features.Advanced", "User", userId);
                await _permissionGrantService.GrantAsync("Features.Export", "User", userId);
                await _permissionGrantService.GrantAsync("Features.API", "User", userId);
                break;

            case "Standard":
                await _permissionGrantService.GrantAsync("Features.Basic", "User", userId);
                await _permissionGrantService.GrantAsync("Features.Export", "User", userId);
                break;

            case "Free":
                await _permissionGrantService.GrantAsync("Features.Basic", "User", userId);
                break;
        }
    }
}
```

### 示例 2: 权限审计日志

```csharp
public class AuditedPermissionChecker : IPermissionChecker
{
    private readonly IPermissionChecker _innerChecker;
    private readonly ILogger<AuditedPermissionChecker> _logger;

    public async Task<bool> IsGrantedAsync(string permissionName)
    {
        var result = await _innerChecker.IsGrantedAsync(permissionName);
        
        _logger.LogInformation(
            "Permission check: {Permission} = {Result} for user {UserId}",
            permissionName, result, GetCurrentUserId());

        return result;
    }
}
```

### 示例 3: 上下文切换

```csharp
public class ImpersonationService
{
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public async Task<T> ExecuteAsUser<T>(string userId, Func<Task<T>> action)
    {
        // 创建模拟用户的 Principal
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("Impersonated", "true")
        };
        var identity = new ClaimsIdentity(claims, "Impersonation");
        var principal = new ClaimsPrincipal(identity);

        // 临时切换上下文
        using (_principalAccessor.Change(principal))
        {
            return await action();
        }
        // 自动恢复原上下文
    }
}
```

### 示例 4: 权限继承

```csharp
public class HierarchicalPermissionChecker
{
    private readonly IPermissionDefinitionManager _definitionManager;
    private readonly IPermissionChecker _permissionChecker;

    public async Task<bool> IsGrantedWithInheritance(string permissionName)
    {
        var permission = _definitionManager.GetOrNull(permissionName);
        if (permission == null)
            return false;

        // 检查当前权限
        if (await _permissionChecker.IsGrantedAsync(permissionName))
            return true;

        // 检查父权限
        var parent = permission.Parent;
        while (parent != null)
        {
            if (await _permissionChecker.IsGrantedAsync(parent.Name))
                return true;

            parent = parent.Parent;
        }

        return false;
    }
}
```

---

## 最佳实践

### ✅ 推荐做法

1. **使用权限而非角色进行授权检查**
   ```csharp
   // ✅ 推荐
   [AuthorizePermission("Products.Delete")]
   
   // ❌ 不推荐
   [Authorize(Roles = "Admin")]
   ```

2. **定义清晰的权限命名约定**
   ```csharp
   // ✅ 好的命名
   "Products.Create"
   "Orders.View"
   "Admin.System.Users.Manage"
   
   // ❌ 糟糕的命名
   "CreateProduct"
   "ViewOrders"
   "ManageUsers"
   ```

3. **使用权限组组织相关权限**
   ```csharp
   var productGroup = context.AddGroup("Products");
   productGroup.AddCrudPermissions("Products");
   ```

4. **为敏感操作添加额外属性**
   ```csharp
   permission.WithProperty("RequireReason", true)
             .WithProperty("RequireTwoFactor", true);
   ```

5. **启用权限缓存以提高性能**
   ```csharp
   services.AddInMemoryPermissionStore(enableCache: true);
   ```

### ❌ 避免做法

1. **不要在代码中硬编码角色名**
   ```csharp
   // ❌ 错误
   if (user.Role == "Admin") { }
   
   // ✅ 正确
   if (await _permissionChecker.IsGrantedAsync("Admin.Access")) { }
   ```

2. **不要跳过权限检查**
   ```csharp
   // ❌ 错误
   public IActionResult DeleteProduct(int id)
   {
       // 直接删除，没有权限检查
       _productService.Delete(id);
   }
   
   // ✅ 正确
   [AuthorizePermission("Products.Delete")]
   public IActionResult DeleteProduct(int id)
   {
       _productService.Delete(id);
   }
   ```

3. **不要在客户端检查权限作为唯一验证**
   ```javascript
   // ❌ 错误：仅在前端检查
   if (hasPermission('Products.Delete')) {
       deleteProduct(id);
   }
   
   // ✅ 正确：前后端都检查
   // 前端：控制UI显示
   // 后端：[AuthorizePermission] 强制验证
   ```

---

## 完整示例

### 示例应用配置

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 添加 RBAC 授权
builder.Services.AddCompleteRbac(
    options =>
    {
        options.EnablePermissionCache = true;
        options.AutoAssignDefaultRoles = true;
    });

builder.Services.AddPermissionDefinitionProvider<AppPermissionDefinitionProvider>();
builder.Services.AddControllers();

var app = builder.Build();

// 初始化权限
await InitializePermissions(app.Services);

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

async Task InitializePermissions(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var permissionStore = scope.ServiceProvider
        .GetRequiredService<InMemoryPermissionStore>();

    // 配置管理员角色权限
    permissionStore.GrantToRole("Admin",
        "Products.Create", "Products.Update", "Products.Delete", "Products.View",
        "Orders.Create", "Orders.Update", "Orders.Delete", "Orders.View");

    // 配置普通用户权限
    permissionStore.GrantToRole("User",
        "Products.View", "Orders.Create", "Orders.View");
}
```

### 权限定义提供者

```csharp
public class AppPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var productGroup = context.AddGroup("Products", "产品管理");
        productGroup.AddCrudPermissions("Products", "产品");

        var orderGroup = context.AddGroup("Orders", "订单管理");
        orderGroup.AddCrudPermissions("Orders", "订单");
    }
}
```

### Controller 示例

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICurrentUser _currentUser;

    [HttpGet]
    [AuthorizePermission("Products.View")]
    public IActionResult GetAll()
    {
        return Ok(new { Message = "产品列表" });
    }

    [HttpPost]
    [AuthorizePermission("Products.Create")]
    public IActionResult Create([FromBody] ProductDto product)
    {
        return CreatedAtAction(nameof(GetAll), new { id = 1 }, product);
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetMyPermissions()
    {
        var permissions = await _permissionChecker.IsGrantedAsync(new[]
        {
            "Products.Create",
            "Products.Update",
            "Products.Delete",
            "Products.View"
        });

        return Ok(new
        {
            UserId = _currentUser.Id,
            UserName = _currentUser.UserName,
            Permissions = permissions.Result
        });
    }
}
```

---

## 参考资源

- [ASP.NET Core Authorization](https://docs.microsoft.com/aspnet/core/security/authorization)
- [Claims-Based Authorization](https://docs.microsoft.com/aspnet/core/security/authorization/claims)
- [Policy-Based Authorization](https://docs.microsoft.com/aspnet/core/security/authorization/policies)

---

**最后更新**: 2025年10月30日  
**版本**: 1.0.0
