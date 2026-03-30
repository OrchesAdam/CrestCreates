# 授权模块优化方案

## 1. 冲突分析

在优化前，CrestCreates框架中的授权模块存在以下冲突和问题：

### 1.1 代码重复
- **AuthorizationAttributeGenerator** 和 **ControllerSourceGenerator** 中存在重复的授权逻辑代码
- 两个生成器都实现了相同的方法：
  - `MapHttpMethodToPermission`：映射HTTP方法到CRUD权限
  - `GetResourceNameFromMethodName`：从方法名提取资源名称
  - `GeneratePermissionAttribute`：生成权限特性代码
  - `GenerateRoleAttribute`：生成角色特性代码

### 1.2 配置系统不一致
- **ServiceAttribute** 和 **AuthorizationConfig** 中的授权配置属性不一致
- **ControllerSourceGenerator** 缺少对 `RequireAuthorizationForAll` 属性的支持
- 两个生成器使用不同的配置方式，导致配置混乱

### 1.3 代码维护困难
- 重复代码导致维护困难，修改一个地方需要同时修改多个地方
- 授权逻辑分散在多个文件中，难以统一管理

## 2. 优化方案

### 2.1 创建公共授权逻辑工具类
创建 `AuthorizationHelper` 静态类，集中管理所有授权相关的公共逻辑：

- **MapHttpMethodToPermission**：统一映射HTTP方法到CRUD权限
- **GetResourceNameFromMethodName**：统一从方法名提取资源名称
- **GeneratePermissionAttribute**：统一生成权限特性代码
- **GenerateRoleAttribute**：统一生成角色特性代码
- **GenerateAuthorizationAttributes**：统一生成完整的授权特性代码

### 2.2 重构生成器使用公共工具类
- **ControllerSourceGenerator**：移除重复方法，使用 `AuthorizationHelper` 中的方法
- **AuthorizationAttributeGenerator**：移除重复方法，使用 `AuthorizationHelper` 中的方法

### 2.3 统一授权配置系统
- 在 **ServiceAttribute** 中添加 `RequireAuthorizationForAll` 属性，与 **AuthorizationConfig** 保持一致
- 在 **ControllerSourceGenerator** 中添加对 `RequireAuthorizationForAll` 属性的支持
- 确保两个生成器使用相同的配置属性和默认值

## 3. 实现细节

### 3.1 公共授权逻辑工具类

```csharp
public static class AuthorizationHelper
{
    public static string MapHttpMethodToPermission(string httpMethod)
    {
        // 实现逻辑
    }

    public static string GetResourceNameFromMethodName(string methodName)
    {
        // 实现逻辑
    }

    public static string GeneratePermissionAttribute(string permission, bool requireAll = false)
    {
        // 实现逻辑
    }

    public static string GenerateRoleAttribute(string[] roles)
    {
        // 实现逻辑
    }

    public static string GenerateAuthorizationAttributes(
        string methodName, 
        string httpMethod, 
        string resourceName, 
        bool generateCrudPermissions, 
        string[] defaultRoles, 
        bool requireAll)
    {
        // 实现逻辑
    }
}
```

### 3.2 重构ControllerSourceGenerator

- 添加对 `AuthorizationHelper` 的引用
- 移除重复的方法实现
- 修改 `GenerateAuthorizationAttributes` 方法，使用 `AuthorizationHelper` 中的方法
- 添加对 `RequireAuthorizationForAll` 属性的支持

### 3.3 重构AuthorizationAttributeGenerator

- 修改 `GenerateAuthorizationAttribute` 方法，使用 `AuthorizationHelper` 中的方法
- 移除重复的私有方法

### 3.4 统一授权配置

- 在 `ServiceAttribute` 中添加 `RequireAuthorizationForAll` 属性
- 在 `ControllerSourceGenerator` 中添加对该属性的支持
- 确保两个生成器使用相同的配置属性和默认值

## 4. 使用方法

### 4.1 基本配置

```csharp
[Service(
    GenerateController = true,
    GenerateAuthorization = true,
    ResourceName = "Product",
    GenerateCrudPermissions = true,
    DefaultRoles = new[] { "Admin", "ProductManager" },
    RequireAll = false,
    RequireAuthorizationForAll = true
)]
public class ProductService
{
    // 方法实现
}
```

### 4.2 配置说明

- **GenerateAuthorization**：是否生成授权特性
- **ResourceName**：资源名称（如 "Product"）
- **GenerateCrudPermissions**：是否生成CRUD权限
- **DefaultRoles**：默认角色要求
- **RequireAll**：是否要求所有权限（AND逻辑）
- **RequireAuthorizationForAll**：是否对所有方法应用授权

## 5. 优势

### 5.1 代码复用
- 消除了重复代码，提高了代码复用性
- 统一了授权逻辑，减少了维护成本

### 5.2 配置一致性
- 统一了授权配置系统，确保两个生成器使用相同的配置属性
- 减少了配置错误的可能性

### 5.3 可维护性
- 集中管理授权逻辑，便于后续扩展和修改
- 提高了代码的可读性和可维护性

### 5.4 性能优化
- 减少了代码冗余，提高了编译和运行性能
- 统一的逻辑实现，减少了运行时的计算开销

## 6. 测试结果

- 项目构建成功，无编译错误
- 生成的控制器代码包含正确的授权特性
- 授权配置系统工作正常

## 7. 总结

通过本次优化，CrestCreates框架的授权模块得到了显著改进：

- 消除了代码重复，提高了代码质量
- 统一了授权配置系统，简化了使用方式
- 提高了代码的可维护性和可扩展性
- 确保了授权逻辑的一致性和可靠性

这些优化措施为框架的长期发展奠定了坚实的基础，同时也为开发者提供了更加简洁、一致的授权配置方式。