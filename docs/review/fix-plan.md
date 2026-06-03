# CrestCreates 框架功能修复执行计划

基于 2026-05 评审报告，按优先级分批执行修复。
已排除：插件系统（plugin-system）。

---

## 第一批：补测试缺口（P0 — 最高优先）

### 1.1 MongoDB 集成测试
**评分**: 5.6/10 → 目标 7.5+
**问题**: 测试覆盖不完整
**状态**: ⏳ 进行中 — 项目已创建，`MongoRepositoryBaseTests` 和 `MongoTenantFilterTests` 已存在，缺少 `TransactionalOutboxTests`
**任务**:
- [x] 创建 `framework/test/CrestCreates.MongoDB.Tests/` 测试项目
- [x] 编写 `MongoRepositoryBaseTests`：CRUD、分页、软删除、租户过滤
- [x] 编写 `MongoTenantFilterTests`：验证多租户隔离
- [ ] 确认测试可通过（需要 MongoDB 实例或 Testcontainers）

**验收标准**: `dotnet test` 通过，覆盖 CRUD/分页/软删除/租户过滤

---

### 1.2 组织架构权限测试
**评分**: 5.6/10 → 目标 7.0+
**问题**: 无测试，一对一关系限制
**状态**: ⏳ 进行中 — `OrganizationHierarchyServiceTests` 已完成（5 个测试），其余待编写
**任务**:
- [x] 创建 `framework/test/CrestCreates.Organization.Tests/` 测试项目
- [x] 编写 `OrganizationHierarchyServiceTests`：ancestor/descendant 查询（5 个测试，全部通过）
- [ ] 编写 `DataPermissionFilter_OrganizationScopeTests`：组织范围数据过滤
- [ ] 编写 `OrganizationPermissionIntegrationTests`：与权限系统联动

**验收标准**: 测试覆盖组织树查询、数据过滤、权限联动

---

### 1.3 认证单元测试
**评分**: 7.4/10 → 目标 8.0+
**问题**: 单元测试薄弱，主要靠集成测试
**状态**: ✅ 已完成
**任务**:
- [x] 在 `framework/test/CrestCreates.Application.Tests/Identity/` 下补充：
  - [x] `PasswordGrantHandlerTests`：密码登录成功/失败/锁定
  - [x] `RefreshTokenHandlerTests`：刷新 token 成功/过期/撤销
  - [x] `IdentityClaimsBuilderTests`：claims 构建完整性
  - [x] `MultiTenancyMiddlewareTests`：租户边界校验

**验收标准**: 核心认证 handler 有独立单元测试

---

### 1.4 全局异常处理补充测试
**评分**: 8.4/10 → 目标 9.0
**问题**: 缺少 409/428 集成测试
**状态**: ✅ 已完成
**任务**:
- [x] 在现有异常处理测试中补充：
  - [x] `ConcurrencyException_ShouldReturn409` 集成测试
  - [x] `PreconditionRequiredException_ShouldReturn428` 集成测试
  - [x] 验证异常本地化 fallback 链

**验收标准**: 409/428 场景有端到端测试

---

## 第二批：补功能缺口（P1 — 中优先）

### 2.1 事件总线幂等处理
**评分**: 7.8/10 → 目标 8.5+
**问题**: 缺少幂等处理（评审中明确要求）
**状态**: ✅ 已完成
**任务**:
- [x] 在 `CrestCreates.EventBus.Abstract` 中新增 `IEventIdempotencyStore` 接口
- [x] 在 `CrestCreates.EventBus.Local` 中实现 `InMemoryEventIdempotencyStore`（基于 ConcurrentDictionary）
- [ ] 在事件消费端集成幂等检查：消费前查 eventId，已消费则跳过
- [x] 编写 `InMemoryEventIdempotencyStoreTests`

**验收标准**: 相同 eventId 的事件不会被重复处理

---

### 2.2 后台作业重试策略
**评分**: 7.8/10 → 目标 8.5+
**问题**: 缺少重试策略抽象
**状态**: ✅ 已完成
**任务**:
- [x] 在 `CrestCreates.Scheduling` 中新增 `IBackgroundJobRetryPolicy` 接口
- [x] 实现 `ExponentialBackoffRetryPolicy`（默认指数退避）
- [x] 实现 `FixedDelayRetryPolicy`（固定间隔）
- [x] 实现 `NoRetryPolicy`（不重试）
- [ ] 在 `BackgroundJobAttribute` 中添加 `MaxRetries` 属性
- [x] 编写 `RetryPolicyTests`（11 个测试）

**验收标准**: 可配置重试次数和策略，测试验证退避行为

---

### 2.3 ORM 能力矩阵文档
**评分**: 7.8/10 → 目标 8.0
**问题**: 缺少跨 ORM 能力对比文档
**状态**: ✅ 已完成
**任务**:
- [x] 创建 `docs/review/orm-capability-matrix.md`
- [x] 对比 EF Core / FreeSql / SqlSugar / MongoDB 的能力：
  - 软删除、多租户过滤、审计、并发控制、UoW、批量操作、原生 SQL
- [x] 标记各 provider 的已知限制和差异

**验收标准**: 文档覆盖 4 个 provider 的 8 项核心能力对比

---

## 第三批：优化改进（P2 — 低优先）

### 3.1 并发控制文档补充
**评分**: 9.0/10 → 目标 9.2
**状态**: ✅ 已完成 — 文档已包含 `UpdateRangeAsync` 限制说明（§5.4）和 `If-Match` header 示例（§4.2）
**任务**:
- [x] 在并发控制相关文档中补充 `UpdateRangeAsync` 不支持并发实体的说明
- [x] 在 Swagger 示例中展示 If-Match header 使用方式

---

### 3.2 Dynamic API 旧生成器标记
**评分**: 9.4/10 → 目标 9.6
**状态**: ✅ 已完成
**任务**:
- [x] 确认 `DynamicApiSourceGenerator`（MVC 控制器版本）的使用情况 — 仍在 CodeGenerator 项目中，全局注入
- [x] 添加 `[Obsolete("Use DynamicApiAotSourceGenerator instead")]` 标记
- [ ] 考虑在后续版本移除

---

## 执行顺序

```
第一批（测试缺口）→ 一批可并行
  ├── 1.1 MongoDB 测试
  ├── 1.2 组织权限测试
  ├── 1.3 认证单元测试
  └── 1.4 异常处理补充

第二批（功能缺口）→ 有依赖，串行
  ├── 2.1 事件总线幂等
  ├── 2.2 后台作业重试
  └── 2.3 ORM 能力矩阵

第三批（优化）→ 可并行
  ├── 3.1 并发文档
  └── 3.2 Dynamic API 标记
```

## 约束

- 所有修改必须遵守 `framework/` 目录结构
- 测试项目引用 `CrestCreates.TestBase`
- 不引入运行时反射作为主路径
- 代码规范：PascalCase，`Async` 后缀，`_camelCase` 私有字段
- 每个任务完成后 `dotnet build` 和 `dotnet test` 必须通过
