# 认证主链

## 概述

CrestCreates 使用 OpenIddict 作为唯一的认证主链，提供标准 OAuth2/OIDC 协议支持。

## 端点

| 端点 | 方法 | 用途 |
|------|------|------|
| `/connect/authorize` | GET/POST | 授权端点 |
| `/connect/token` | POST | 令牌端点（密码授权、刷新令牌、客户端凭证） |
| `/connect/userinfo` | GET/POST | 用户信息端点 |
| `/connect/logout` | GET/POST | 登出端点 |

## 支持的授权类型

| 授权类型 | grant_type | 用途 |
|----------|------------|------|
| Password Grant | `password` | 用户密码登录 |
| Client Credentials Grant | `client_credentials` | 客户端凭证（机器对机器） |
| Refresh Token Grant | `refresh_token` | 刷新访问令牌 |

## Token 请求示例

### 密码登录

```bash
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&username=admin&password=Admin123!&scope=openid%20profile%20email
```

### 刷新令牌

```bash
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token&refresh_token=<refresh_token>
```

## Token Claims 结构

```json
{
  "sub": "user-id-guid",
  "name": "user-name",
  "email": "user@example.com",
  "tenantid": "tenant-id-guid",
  "is_super_admin": "true",
  "role": ["Admin", "User"]
}
```

**注意**：Token 中不包含 permission 相关 claims，权限在运行时动态查询。

## 租户边界

### 分离的租户来源

| 来源 | 读取位置 | 含义 |
|------|----------|------|
| `CurrentUser.TenantId` | Token 内嵌的 `tenantid` claim | 用户登录时所属的租户 |
| `CurrentTenant.Id` | 请求上下文解析（Header/Subdomain/Query） | 当前请求的目标租户上下文 |

### 校验规则

1. **登录时校验**：用户所属租户必须与请求租户上下文一致
2. **运行时校验**：`TenantBoundaryMiddleware` 校验 `CurrentUser.TenantId == CurrentTenant.Id`
3. **超级管理员**：超级管理员可跨租户访问

## 权限系统

权限在运行时动态查询，不存储在 Token 中。

### 查询权限

```csharp
var isGranted = await _permissionChecker.IsGrantedAsync(
    userId: currentUser.Id,
    permissionName: "Book.Search",
    tenantId: currentUser.TenantId);
```

### 权限来源

- 用户直接授权
- 角色授权（通过角色继承）
- 超级管理员绕过权限检查

## 配置

### Startup.cs

```csharp
services.AddOpenIddictServer(options =>
{
    options.EnablePasswordFlow = true;
    options.EnableClientCredentialsFlow = true;
    options.EnableRefreshTokenFlow = true;
    options.AccessTokenLifetimeMinutes = 60;
    options.RefreshTokenLifetimeDays = 14;
});
services.AddOpenIddictAuthentication();
```

### 中间件顺序

```csharp
app.UseMultiTenancy();      // 1. 解析租户上下文
app.UseAuthentication();     // 2. Token 校验（OpenIddict Validation）
app.UseTenantBoundary();     // 3. 租户边界校验
app.UseAuthorization();      // 4. 权限校验
```

## 迁移说明

### 从 AuthController 迁移

旧端点已废弃，请使用 OpenIddict 端点：

| 旧端点 | 新端点 |
|--------|--------|
| POST /api/auth/login | POST /connect/token (grant_type=password) |
| POST /api/auth/refresh-token | POST /connect/token (grant_type=refresh_token) |
| GET /api/auth/me | GET /connect/userinfo |
| POST /api/auth/logout | POST /connect/logout |
