# 平台管理 HTTP 契约（Platform Management HTTP Contract）

正式主链通过 CapabilityEndpoint ExplicitHost 投影暴露平台管理面（G-09 补齐）。本文档是消费者
（前端/集成方）的权威契约：路由矩阵、响应信封、枚举约定、激活方式与权限目录。

## 激活

平台投影锚点位于 `src/Platform/CrestCreates.Web/PlatformManagement/`，构建期由 AppServiceHttp
precompile + JsonContracts 自动闭合（零构建配置改动）。宿主**显式 opt-in**：

```csharp
builder.AddCrestWeb();                       // 已含 Identity/Permission/Setting/Feature/Tenant/AuditLogging DI
builder.Services.AddCapabilityRuntime();
builder.Services.AddSingleton<ISchemaValidator, SchemaValidator>();
builder.Services.AddCrestCapabilityEndpoints();
builder.Services.AddCrestPlatformManagementHttp();   // 平台管理面（含 AuditRetention，程序集聚合、幂等）
// ... UseCrestWeb() → 提升 contribution JsonContext → MapCrestCapabilityEndpoints() → MapCrestWeb()
```

- `AddCrestPlatformManagementHttp()` 转调程序集聚合激活
  `GeneratedAppServiceHttpActivation_CrestCreates_002EWeb.AddGeneratedAppServiceHttp()`，
  与 `AddCrestAuditRetentionHttp()` 同一入口、幂等；激活即连带 AuditRetention 投影。
- 不进 `AddCrestWeb` 默认，保 ExplicitHost opt-in 语义与既有激活基线。
- AuditRetention 路由的**执行**另需 `AddAuditRetention()` + PG retention store +
  `AddSettingDefinitionProvider<AuditLoggingSettingDefinitionProvider>()`；未组合时路由存在但执行失败（边界）。

## 响应信封

- 成功：`{ "code": 200, "message": "操作成功", "data": <T> }`（camelCase；`DynamicApiResponse<T>`，
  `CompatibilityHttpResultMapper` 包装）。GET 未命中 → 404 typed 信封。
- 失败：`{ "code": <string>, "message": <string>, "details": <string?>, "traceId": <string>, "statusCode": <int> }`
  （`CrestErrorResponse`）。权限不足 403 的 `message` 为 `"没有权限执行当前操作。"`。

## 枚举约定（G-07）

- **输出**：HTTP/CRUD 生成的 JSON 上下文启用 `UseStringEnumConverter=true`，枚举序列化为**字符串名**
  （如 `MaterialType.Manufactured` → `"Manufactured"`，`SettingScope.Tenant` → `"Tenant"`）。
- **输入**：route/query 枚举经 `Enum.TryParse(ignoreCase)` 容忍字符串名与数字；JSON body 经 string
  converter 读字符串名（默认亦容忍数字）。
- 事件 codec（LocalEvent/IntegrationEvent）契约独立，本轮未改。

## 路由矩阵（干净 REST）

写操作均 POST/PUT/PATCH/DELETE（`Kind=Command`，审计语义正确）；读 GET（`Kind=Query`）。
`current-user`/`current-tenant` 系列与 `change-password`/`is-enabled` 仅要求认证（Permissions 留空，服务层把关）。

| 服务 | prefix | 路由 |
|---|---|---|
| User | `/api/users` | POST `""`；GET `"{userId}"`；GET `""`(?tenantId)；PUT `"{userId}"`；PATCH `"{userId}/active"`(?isActive)；POST `"{userId}/change-password"`；POST `"{userId}/roles/{roleId}"`；DELETE `"{userId}/roles/{roleId}"`；GET `"{userId}/roles"` |
| Role | `/api/roles` | POST `""`；GET `"{roleId}"`；GET `""`；PUT `"{roleId}"`；PATCH `"{roleId}/active"`(?isActive) |
| PermissionGrant | `/api/permission-grants` | POST `"user/{userId}"`(body)；POST `"role/{roleName}"`(body)；DELETE `"user/{userId}"`(body)；DELETE `"role/{roleName}"`(body)；GET `"user/{userId}"`；GET `"role/{roleName}"`；GET `"effective/{userId}"`(?tenantId) |
| Setting | `/api/settings` | GET `current`/`global`/`current-tenant`/`tenant/{tenantId}`/`current-user`/`user/{userId}`(?groupName,?tenantId)；PUT `global/{name}`/`current-tenant/{name}`/`tenant/{tenantId}/{name}`/`current-user/{name}`/`user/{userId}/{name}`(body `{value}`,?tenantId)；DELETE 同五路 |
| SettingDefinition | `/api/setting-definitions` | GET `"groups"`；GET `""`；GET `"{name}"` |
| Feature | `/api/features` | GET `global`/`tenant/{tenantId}`/`current-tenant`/`global/{name}`/`tenant/{tenantId}/{name}`/`current-tenant/{name}`；PUT `global/{name}`(?value)/`tenant/{tenantId}/{name}`(?value)；DELETE `global/{name}`/`tenant/{tenantId}/{name}`；GET `enabled/{featureName}`/`tenant/{tenantId}/enabled/{featureName}` |
| FeatureDefinition | `/api/feature-definitions` | GET `""`；GET `"{name}"`；GET `"groups"` |
| Tenant | `/api/tenants` | POST `""`；GET `"{name}"`；GET `""`(?isActive)；PUT `"{name}"`；PATCH `"{name}/active"`(?isActive)；POST `"{tenantId}/retry-initialization"`；GET `"{tenantId}/initialization-status"`；POST `"{tenantId}/force-retry-initialization"`；POST `"{tenantId}/force-fail-initialization"` |
| AuditLog | `/api/audit-logs` | GET `""`(?keyword&status&httpMethod&tenantId&userId&userName&startTime&endTime&pageIndex&pageSize)（self-contract 扁平 query） |

AuditLog 查询用 self-contract 投影类（`AuditLogHttpProjection`）扁平化标量 query 组装
`AuditLogPagedRequestDto`——复杂 DTO 无法绑 query（`BookHttpProjection` 先例）。`Sorts/Filters` 复杂列表不上 query。

## 权限目录

| 常量 | 值 | 用于 |
|---|---|---|
| Users.Read / Manage / ManageSuperAdmin | `IdentityManagement.Users.*` | User/Role（Role 复用 Users 域） |
| PermissionGrants.Read / Manage | `PermissionManagement.*` | PermissionGrant |
| Settings.Read / Manage | `SettingManagement.*` | Setting/SettingDefinition（Definition 复用 Read） |
| Features.Read / ManageGlobal / ManageTenant | `FeatureManagement.*` | Feature/FeatureDefinition；管理类 host-only（租户上下文 403） |
| Tenants.View / Update | `TenantManagement.*`（新增，对齐 bootstrap） | Tenant |
| AuditLog.Read | `AuditLogging.Read`（新增） | AuditLog 查询 |

wire 常量在 `CrestCreates.Application.Contracts/Permissions/PlatformHttpPermissions.cs`（Contracts 不能反引
Application）；Application 层 admission 常量值与之逐字一致。双层防线：HTTP 侧锚点 Permissions →
`PermissionCapabilityAuthorizationService`（AllGranted）；服务层 `IPermissionChecker.CheckAsync`
（覆盖 job/直接 DI 调用）。Role/SettingDefinition/Tenant/AuditLog 实现已补 admission。

## 支持声明边界

JIT 运行证据；不新增 NativeAOT/trim 声明。租户隔离仍由仓储/过滤器层保证（AuditLog 租户上下文忽略越权
tenantId 参数）。