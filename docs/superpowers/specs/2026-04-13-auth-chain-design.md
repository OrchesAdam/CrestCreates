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

## 9. R3-06：密钥、刷新与注销契约（2026-11-11）

当前正式 HTTP 入口是 `MapCrestOpenIddictEndpoints`，上文 Controller 名称属于原设计记录。
本切片统一 `OpenIddictModule` 和 `AddOpenIddictServer`：同一 service collection 只装配一次，
module 先注册、Host 后配置也消费同一个配置对象，不再创建第二套无密钥 server。

### 密钥配置与迁移

- 默认不生成密钥；缺 signing 或 encryption 材料会在 Host 启动的 options validation 阶段失败。
- 支持调用方提供 `SigningCertificates` / `EncryptionCertificates`，或 `SigningKeys` /
  `EncryptionKeys`；本版本这些集合接受 **RSA 私钥、至少 2048 bits**，分别使用 RS256 和
  RSA-OAEP + A256CBC-HS512。X509 若声明 key usage，必须分别允许 DigitalSignature / KeyEncipherment，
  与官方证书注册要求一致。程序传入的对象由调用方持有到 Host 结束；框架不释放它们。
- `AddCrestWeb` 与 LibraryManagement 绑定 `OpenIddict` 配置节。配置文件证书由
  `SigningCertificateFiles` / `EncryptionCertificateFiles` 指定；框架读取 PKCS#12，私钥只加载到
  内存并由容器释放。证书路径/密码/底层加载错误不会进入框架的配置异常消息。
- `Issuer` 已生效；显式值要求绝对 HTTPS URI，不能带 query/fragment。多 Host 必须配置相同
  issuer、相同可验证/解密的密钥集合和共享 OpenIddict store。未设置时保留 OpenIddict 按请求推导
  issuer 的行为，不因此承诺不同地址 Host 可互认。
- 平台仍默认 access 60 分钟、refresh 14 天；独立扩展的 refresh 默认仍为 7 天，配置可覆盖。

无密码的外部证书配置形状如下；有密码的 PFX 通过受控配置提供
`OpenIddict__SigningCertificateFiles__0__Password` / `OpenIddict__EncryptionCertificateFiles__0__Password`，
不要将密码、PFX 或私钥提交到仓库。

```json
{
  "OpenIddict": {
    "Issuer": "https://identity.example.com/",
    "SigningCertificateFiles": [{ "Path": "/run/secrets/oidc-signing.pfx" }],
    "EncryptionCertificateFiles": [{ "Path": "/run/secrets/oidc-encryption.pfx" }]
  }
}
```

轮换使用重叠集合：先把新旧验证/解密材料部署到所有节点，再切换签发材料；待旧 token、refresh、
code 等受保护工件全部到期后才移除旧材料。框架沿用 OpenIddict 的 credential 排序/选择规则；
X509 通常优先较晚到期的有效证书，普通 RSA key 在同类排序下按注册顺序选择。
新签名 key 的 `kid` 必须可区分。配置在启动时加载，修改后重启 Host；不承诺热轮换。
过去仅由 ephemeral key 保护的 token 无法在丢失私钥后恢复，迁移需要重新登录。

### 开发公共客户端

`EnableDevelopmentEphemeralKeys` 与 `EnableDevelopmentPublicPasswordClient` 是两个独立、默认 false
的开关，任一为 true 都要求注入的 `IHostEnvironment.IsDevelopment()`；Production/Staging 明确拒绝。
前者只补缺失的密钥集合；后者允许播种开发公共 password client，并要求 password flow 开启。
两个 sample 只在 `appsettings.Development.json` 中显式开启，生产配置必须提供真实密钥。

公共客户端不再被播种 `client_credentials` 权限。password grant 入口还会通过官方 application manager
读取 client type，拒绝未获开发 opt-in 的公共客户端，拒绝发生在用户仓储/密码校验之前；这也覆盖
数据库里遗留的公共 password client。合法 confidential password client 仍使用官方 secret 校验。
手动构造 `HostOpenIddictDataSeeder` 的调用方需补入 `OpenIddictServerOptions` 与 `IHostEnvironment`；
默认 DI 由统一扩展提供配置实例。
本切片不自动删除或改写存量客户端；部署方需使用官方 managers 撤销旧授权/refresh、移除不再需要的
开发客户端及其旧权限，再给正式消费者注册合适的客户端。既有 refresh 不因配置切换自动全部撤销。

### 刷新身份与租户

刷新只接受官方 OpenIddict server 验证后的 principal，再读取当前用户与角色仓储：
启用/锁定检查后，使用既有 `IIdentityClaimsBuilder` 重建用户名、邮箱、组织、SuperAdmin 和启用的
同租户角色。旧角色、自定义权限及其他应用 claims 不复制；权限仍由运行时 `IPermissionChecker` 查询。
仅克隆 OpenIddict 官方私有 `oi_` 协议 claims，以保留 nonce、单令牌 lifetime override 等元数据；
应用身份 claims 全部重新生成。scopes、resources、authorization id、presenters 另通过官方 principal APIs
显式传递，保持会话/客户端绑定。令牌派生身份/时间仍由 OpenIddict 正式 SignIn pipeline 处理。

要求 token 的 `tenant_id`、用户权威 `TenantId`、`ICurrentTenant.Id` 一致；`tenantid` 别名若出现也必须
一致，重复 tenant claim 拒绝。host 用户只能在 host 上下文刷新；tenant 用户不能在无 tenant 上下文
刷新，请求必须按既有 resolver 提供该租户。这里不新增按名称猜测、host 旁路或另一套 tenant provider。
Refresh handler 构造函数新增 `IRoleRepository` / `ICurrentTenant` / `IIdentityClaimsBuilder` 依赖，手动构造
调用方需要迁移；默认 DI 使用这些既有契约。

### 注销 API 迁移

- `POST /connect/logout` 要求显式 Bearer header，并通过 OpenIddict Validation 验证。匿名/无效 token
  返回 401；cookie 本身不能触发撤销。撤销目标从已验证 token 的 store entry 推导，忽略任何 userId /
  authorizationId 等调用方参数。
- 官方 token/authorization managers 核对 subject，撤销该 token 关联的 authorization 及其 token family；
  无 authorization 时仅撤销当前 token。无可验证的 store entry 返回 403，撤销冲突返回 409。
- `GET /connect/logout` 已移除（405），客户端必须迁移到带 Bearer 的 POST。这是 token-family 撤销，
  不承诺浏览器 cookie 清除或 OIDC end-session 重定向协议。
- 不承诺已签发 access token 立即失效：当前 local validation 没有开启 token-entry 每请求校验。
  已撤销 family 的 refresh 被 server 拒绝，但 access token 可持续到原有效期；另一 authorization 的
  会话不会被按 userId 批量撤销。

### 证据边界

验证使用 SDK `/home/orches/.dotnet/dotnet` 10.0.114，项目及 xUnit collection 串行执行。
新增回归先复现了 production 无密钥仍成功解析配置、refresh 复用旧 principal 两个失败；
修复后配置/租户/身份单元测试及真实 Kestrel + PostgreSQL OpenIddict store 测试见 `memory.md`。
后者实际覆盖 R3-06-T01–T06：跨 Host、Host 实例重启、旧签名/解密 key 轮换过渡、停用后拒绝、
角色/SuperAdmin/组织重建、注销后 refresh 拒绝与另一 family 保留；密钥由 fixture 外部提供并跨实例复用。
用户/角色仓储、密码校验和安全日志为测试替身，因此不推导真实 Identity EF repository、租户 middleware、
R3-46 新库迁移闭包、R3-52 并发锁定，也不推导独立 OS 进程重启、跨节点撤销竞争、浏览器会话或完整
authorization-code/OIDC refresh 协议矩阵。配置测试另用真实临时 PFX 验证平台绑定与证书加载。
本次是 linux-x64 JIT 执行证据；没有认证链 NativeAOT publish/link/run，不新增原生支持声明。
