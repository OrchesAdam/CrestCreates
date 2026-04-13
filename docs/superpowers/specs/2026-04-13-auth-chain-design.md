# 认证主链边界设计

**日期**: 2026-04-13
**状态**: 已批准

## 1. 背景与目标

CrestCreates 框架当前存在两套认证入口并存的局面：
- `OpenIddictController` (`/connect/*`) - OAuth2/OpenID Connect 标准端点
- `AuthController` (`/api/auth/*`) - 自定义 JWT 端点

这种双入口设计导致职责边界不清晰，需要明确认证主链边界。

## 2. 核心决策

| 维度 | 选择 |
|------|------|
| 认证协议主链 | OpenIddict |
| Token 校验主链 | OpenIddict Validation（非 JwtBearer） |
| 身份校验/安全策略 | 服务层能力，下沉为 OpenIddict 内部依赖 |
| 权限真相来源 | 运行时授权查询，Token 无 permission 语义 |
| 租户模型 | 分离模型 + 一致性校验 |
| 旧入口处理 | 阶段式迁移：迁移 → 删除 → 决策 |

## 3. 认证主链架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           认证主链架构                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      OpenIddict 主链                             │  │
│  │                                                                  │  │
│  │  ┌──────────────┐    ┌──────────────────┐    ┌───────────────┐ │  │
│  │  │   登录入口   │    │   Token 签发     │    │ Token 校验    │ │  │
│  │  │              │    │                  │    │               │ │  │
│  │  │ OpenIddict   │───▶│ OpenIddict       │───▶│ OpenIddict    │ │  │
│  │  │ Controller   │    │ Token Endpoint   │    │ Validation    │ │  │
│  │  │              │    │                  │    │               │ │  │
│  │  │ /connect/    │    │ Password Grant,  │    │ 校验签名/过期 │ │  │
│  │  │  authorize   │    │ Refresh Grant,   │    │ 解析 Claims   │ │  │
│  │  │  token       │    │ Client Creds     │    │               │ │  │
│  │  └──────────────┘    └──────────────────┘    └───────────────┘ │  │
│  │                                                                  │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      租户边界校验层                               │  │
│  │                                                                  │  │
│  │  CurrentUser.TenantId ──────┬───▶ Claims (Token 内嵌)            │  │
│  │                              │                                   │  │
│  │  CurrentTenant.Id ───────────┴───▶ 请求上下文解析                │  │
│  │                              │                                   │  │
│  │                              ▼                                   │  │
│  │                     TenantBoundary 校验一致性                    │  │
│  │                     登录时校验：用户所属租户 = 请求租户           │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      权限装载层                                   │  │
│  │                                                                  │  │
│  │  IPermissionChecker.IsGrantedAsync(userId, permission)          │  │
│  │         │                                                       │  │
│  │         ▼                                                       │  │
│  │  运行时查询数据库/缓存 ── 无 Token 内嵌权限语义                  │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## 4. 组件职责定义

| 组件 | 职责 | 状态 |
|------|------|------|
| `OpenIddictController` | 唯一登录入口：`/connect/authorize`、`/connect/token`、`/connect/userinfo`、`/connect/logout` | ✅ 正式主链 |
| `OpenIddictTokenEndpoint` | 签发 Token，内嵌 `sub`、`tenant_id`、`name`、`email` claims（无 permission） | ✅ 正式主链 |
| `OpenIddictValidationHandler` | 唯一 Token 校验入口 | ✅ 正式主链 |
| `CurrentUser` | 从 `ClaimsPrincipal` 读取用户 ID 和租户 ID | ✅ 正式主链 |
| `ITenantResolver` / `CurrentTenant` | 从请求上下文解析租户 | ✅ 正式主链 |
| `TenantBoundary` | 校验 `CurrentUser.TenantId == CurrentTenant.Id`，登录时校验用户属于请求租户 | ✅ 正式主链 |
| `IPermissionChecker` | 唯一权限查询入口，运行时查询 | ✅ 正式主链 |
| `IdentitySecurityService` | 身份校验、安全策略（从 AuthService 迁移） | ✅ 正式主链 |
| `JwtBearer*` | 退出主链，迁移阶段短期保留用于过渡验证 | 🔄 迁移中 |
| `AuthController` | 迁移阶段保留，OpenIddict 接管后删除 | 🔄 迁移中 |
| `AuthService` | 迁移阶段保留，能力迁移后决策删除或下沉 | 🔄 迁移中 |

## 5. Token Claims 结构

```json
{
  "sub": "user-id-guid",
  "name": "user-name",
  "email": "user@example.com",
  "tenant_id": "tenant-id-guid",
  "is_super_admin": "false",
  "iat": 1234567890,
  "exp": 1234571490,
  "iss": "CrestCreates",
  "aud": "CrestCreates.Client"
}
```

**注意**：Token 中不包含 permission 相关 claims，权限在运行时动态查询。

## 6. 租户模型

### 分离的租户来源

| 来源 | 读取位置 | 含义 |
|------|----------|------|
| `CurrentUser.TenantId` | Token 内嵌的 `tenant_id` claim | 用户登录时所属的租户 |
| `CurrentTenant.Id` | 请求上下文解析（Header/Subdomain/Query） | 当前请求的目标租户上下文 |

### TenantBoundary 校验

1. **运行时校验**: `CurrentUser.TenantId == CurrentTenant.Id`
2. **登录时校验**: 用户.tenant_id == 请求上下文解析的租户 ID

## 7. 迁移步骤

### 阶段 1：能力迁移（OpenIddict 接管）

1. OpenIddict 扩展实现：
   - `/connect/userinfo` → 用户信息服务
   - `/connect/logout` → 登出能力
   - Password Grant → 登录能力

2. 迁移 IdentitySecurityService：
   - 从 AuthService 剥离 → 成为 OpenIddict 内部依赖

3. 验证：OpenIddict 完整覆盖原 AuthController 能力

### 阶段 2：入口删除

1. 移除 `AuthController`（/api/auth/*）
2. JwtBearer 模块 → 过渡验证完成后删除
3. 验证：所有调用迁移到 OpenIddict 端点

### 阶段 3：服务决策

1. 审计 AuthService 各能力：
   - 密码校验 → 可复用 → 迁移为 `IdentitySecurityService`（内部服务）
   - 密码哈希 → 可复用 → 迁移为 `PasswordHasher`（内部服务）
   - Token 管理 → OpenIddict 自有 → 删除
   - 其他能力 → 逐项评估可复用性

2. AuthService 本身：
   - **不作为长期正式抽象保留**

3. 最终状态：
   - OpenIddict 主链完整
   - 可复用能力下沉为明确内部服务
   - AuthService 彻底移除

## 8. 验收清单

| 验收点 | 状态 |
|--------|------|
| 登录入口唯一：`OpenIddictController` (`/connect/token`) | ✅ |
| Token 签发入口唯一：OpenIddict Token Endpoint | ✅ |
| Token 校验入口唯一：`OpenIddictValidationHandler` | ✅ |
| 权限来源唯一：`IPermissionChecker` 运行时查询，Token 无 permission | ✅ |
| 租户来源唯一：分离模型 + `TenantBoundary` 校验 | ✅ |
| JwtBearer 处理：退出主链，过渡验证后删除 | ✅ |
| 旧入口迁移：阶段式迁移 | ✅ |
| AuthService 处理：不作为正式抽象，可复用能力下沉 | ✅ |
