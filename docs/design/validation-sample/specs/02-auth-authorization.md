# Spec: Auth & Authorization

## 概述

使用 OpenIddict 提供 JWT Bearer Token 认证。角色模型复用框架 `IdentityRole`，用户复用 `IdentityUser`。不新增自定义认证真相来源。

## 认证配置

```csharp
// OpenIddict 服务端配置
AddOpenIddictServer(options => {
    options.AddEphemeralEncryptionKey()   // 开发环境
           .AddEphemeralSigningKey()
           .AllowPasswordFlow()            // 用户名密码登录
           .AllowRefreshTokenFlow()        // Refresh Token
           .AcceptAnonymousClients()
           .UseAspNetCore()
           .EnableTokenEndpointPassthrough()
           .EnableAuthorizationEndpointPassthrough();
});

// JWT Bearer 认证配置
AddOpenIddictAuthentication(options => {
    options.AddEphemeralEncryptionKey()
           .AddEphemeralSigningKey()
           .UseAspNetCore()
           .EnableTokenEndpointPassthrough();
});
```

## API

| 方法 | 路径 | 说明 |
|------|------|------|
| `POST` | `/api/auth/login` | 用户名密码登录 → 返回 access_token + refresh_token |
| `POST` | `/api/auth/refresh` | 用 refresh_token 获取新 access_token |
| `GET` | `/api/auth/me` | 获取当前用户信息 |

### Login Request/Response

```json
// POST /api/auth/login
{
    "userName": "agent@acme.com",
    "password": "***"
}

// Response 200
{
    "accessToken": "eyJ...",
    "tokenType": "Bearer",
    "expiresIn": 3600,
    "refreshToken": "CfD..."
}
```

### Refresh Request/Response

```json
// POST /api/auth/refresh
{
    "refreshToken": "CfD..."
}

// Response 200
{
    "accessToken": "eyJ...",
    "tokenType": "Bearer",
    "expiresIn": 3600,
    "refreshToken": "CfD..."  // 新的 refresh_token (rotation)
}
```

## 角色与权限矩阵

### 角色定义（每个租户独立）

| 角色 | 标识 | 说明 |
|------|------|------|
| 客服 (Agent) | `Helpdesk.Agent` | 处理分配给自己的工单 |
| 客服主管 (Supervisor) | `Helpdesk.Supervisor` | 管理团队、分配工单 |
| 管理员 (Admin) | `Helpdesk.Admin` | 租户管理、系统配置 |

### 权限定义

```csharp
public static class HelpdeskPermissions
{
    // Tickets
    public const string Tickets = "Helpdesk.Tickets";
    public const string Tickets_Create = "Helpdesk.Tickets.Create";
    public const string Tickets_Update = "Helpdesk.Tickets.Update";
    public const string Tickets_Delete = "Helpdesk.Tickets.Delete";
    public const string Tickets_Assign = "Helpdesk.Tickets.Assign";   // 分配工单给客服
    public const string Tickets_ViewAll = "Helpdesk.Tickets.ViewAll";  // 查看所有工单（非仅有自己）

    // Customers
    public const string Customers = "Helpdesk.Customers";
    public const string Customers_Create = "Helpdesk.Customers.Create";
    public const string Customers_Update = "Helpdesk.Customers.Update";
    public const string Customers_Delete = "Helpdesk.Customers.Delete";

    // Agents (客服人员管理)
    public const string Agents = "Helpdesk.Agents";
    public const string Agents_Create = "Helpdesk.Agents.Create";
    public const string Agents_Update = "Helpdesk.Agents.Update";
    public const string Agents_Deactivate = "Helpdesk.Agents.Deactivate";

    // Categories
    public const string Categories = "Helpdesk.Categories";
    public const string Categories_Manage = "Helpdesk.Categories.Manage";

    // SLA Policies
    public const string SLAPolicies = "Helpdesk.SLAPolicies";
    public const string SLAPolicies_Manage = "Helpdesk.SLAPolicies.Manage";

    // Knowledge Base
    public const string KnowledgeBase = "Helpdesk.KnowledgeBase";
    public const string KnowledgeBase_Read = "Helpdesk.KnowledgeBase.Read";
    public const string KnowledgeBase_Manage = "Helpdesk.KnowledgeBase.Manage";

    // Dashboard
    public const string Dashboard = "Helpdesk.Dashboard";
    public const string Dashboard_View = "Helpdesk.Dashboard.View";

    // Settings & Features
    public const string Settings = "Helpdesk.Settings";
    public const string Settings_Manage = "Helpdesk.Settings.Manage";
}
```

### 权限 - 角色映射

| 权限 | Agent | Supervisor | Admin |
|------|:-----:|:----------:|:-----:|
| `Tickets.Create` | ✓ | ✓ | ✓ |
| `Tickets.Update` | 仅自己工单 | ✓ | ✓ |
| `Tickets.Delete` | ✗ | ✗ | ✓ |
| `Tickets.Assign` | ✗ | ✓ | ✓ |
| `Tickets.ViewAll` | ✗ | ✓ | ✓ |
| `Customers.*` | 读 | ✓ | ✓ |
| `Agents.*` | ✗ | ✗ | ✓ |
| `Categories.Manage` | ✗ | ✗ | ✓ |
| `SLAPolicies.Manage` | ✗ | ✓ | ✓ |
| `KnowledgeBase.Read` | ✓ | ✓ | ✓ |
| `KnowledgeBase.Manage` | ✗ | ✓ | ✓ |
| `Dashboard.View` | ✗ | ✓ | ✓ |
| `Settings.Manage` | ✗ | ✗ | ✓ |

## AppService 权限声明

```csharp
[CrestService]
[Authorize]
public class TicketAppService : CrestAppServiceBase<Ticket, Guid, TicketDto, CreateTicketDto, UpdateTicketDto>,
    ITicketAppService
{
    [Authorize(HelpdeskPermissions.Tickets_Create)]
    public override async Task<TicketDto> CreateAsync(CreateTicketDto input) { ... }

    [Authorize(HelpdeskPermissions.Tickets_Assign)]
    public async Task<TicketDto> AssignAsync(Guid ticketId, Guid agentId) { ... }
}
```

## Permission Sync

启动时自动执行：将编译期生成的权限清单同步到数据库 `PermissionGrants` 表。

```csharp
// 框架配置
services.AddPermissionSync();  // 注册 PermissionSyncHostedService
```

## 验证检查点

- [ ] 无 Token 访问 Protected API 返回 401
- [ ] Agent 角色无法调用 `AssignAsync` (需要 `Tickets.Assign`)
- [ ] Agent 角色无法查看非自己处理的工单 (需要 `Tickets.ViewAll`)
- [ ] Admin 角色可访问所有 API
- [ ] Refresh Token 正常刷新，旧 refresh_token 作废 (rotation)
- [ ] Permission Sync 将生成权限写入 DB 的 PermissionGrants 表
- [ ] `ICurrentUser` 正确返回当前用户 Id 和 Claims
- [ ] `IPermissionChecker.IsGrantedAsync()` 正确拦截
