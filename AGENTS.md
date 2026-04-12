# AGENTS.md

## 项目定位

CrestCreates 是一个类 ABP Framework 的 .NET 企业级应用开发框架。

当前阶段最重要的工程目标不是“继续堆模块名”，而是把框架主链做扎实：

- 优先使用编译期代码生成，减少运行时反射
- 优先保证 AoT 友好
- 优先收口唯一主链，避免双轨实现长期并存
- 优先做可复用的平台能力，而不是业务级补丁实现

---

## 第一原则

### 1. 第一性原理

必须从原始目标出发，不要直接沿用已有实现习惯。

如果一个需求的目标是：
- 减少反射
- 提高 AoT 兼容性
- 提升框架一致性

那么实现时就不能继续把运行时扫描、反射调用、兼容性 fallback 当作正常主路径。

### 2. 最短正确路径

不允许：
- 兼容性方案
- 补丁性方案
- 双轨长期并存
- 兜底式设计
- 超出需求的扩展

允许为了落地做过渡，但过渡必须明确、短期、可移除，不能变成正式主链。

### 3. 唯一主链

同一能力如果已经确定主实现，就不要再维护第二套“也能跑”的实现。

当前项目里，这条原则尤其适用于：
- Dynamic API
- 认证链路
- 模块构建 / 初始化链路
- 租户创建 / 初始化链路

---

## 当前架构共识

### 1. Dynamic API

Dynamic API 的长期方向已经明确：

- 主链必须是 **Compile-time Generated**
- 主执行路径必须是 **Generated Endpoints**
- 默认不能依赖 runtime reflection scanner / executor

因此：

- `DynamicApiScanner`
- `DynamicApiEndpointExecutor`
- runtime reflection fallback

都不应再被当作一等公民长期维护。

如果修改 Dynamic API：
- 优先改 SourceGenerator、Generated Runtime、Generated Registry
- 不要继续给 runtime scanner / executor 加新能力
- 新测试也应优先验证 generated path

### 2. 模块系统

模块初始化主链依赖：

- `CrestCreates.CodeGenerator`
- `CrestCreates.BuildTasks`
- 编译期生成的模块聚合初始化代码

因此不要再把“运行时扫描模块”当作框架真实主链来设计。

### 3. Setting Management

Setting Management 已经是正式平台能力的一部分。

后续涉及运行时可管理配置时：
- 优先接 Setting Management
- 不要重新造一套 ad-hoc 配置表
- 不要绕开现有 Setting 定义、作用域、缓存、加密链路

### 4. 多租户

多租户当前已经不只是“请求解析”：
- 有租户创建/初始化主链
- 有租户生命周期
- 有租户隔离规则

后续改动必须遵守：
- Tenant 统一使用 `TenantId`
- 不要混用 `TenantName` 作为上下文主键
- 新能力必须能和 `CurrentTenant` 主链对齐

### 5. 认证授权

认证和授权已经在往平台化链路收口。

新增能力时：
- 不要再引入新的认证真相来源
- 不要复制 token / claims / permission 逻辑
- 优先复用现有身份、权限、租户上下文主链

---

## 代码规范

### 1. 命名规范

当编写任何 C# 代码时：

- 类名、接口名、属性名、方法名使用 PascalCase
- 类名必须是名词或名词短语
- 异步方法以 `Async` 结尾
- 私有字段使用 `_camelCase`

### 2. 分层要求

遵守现有分层：

- `Domain.Shared`
- `Domain`
- `Application.Contracts`
- `Application`
- `Infrastructure`
- `OrmProviders.*`
- `Web / AspNetCore`
- `test`

不要把领域抽象直接塞进 Web 层。
不要把应用编排逻辑直接塞进仓储。
不要把平台能力实现成 sample 特例。

### 3. 依赖方向

保持依赖方向清晰：

- Contracts 不依赖 Application 实现
- Domain 不依赖 Web
- Infrastructure 是实现，不应反过来定义核心业务抽象

### 4. 注释

注释只解释复杂逻辑或设计意图。
不要写废话注释。

---

## 实现偏好

### 1. 优先代码生成，不优先反射

如果一个能力既可以：
- 编译期生成
- 运行时扫描

优先前者。

只有在明确无法走生成链时，才允许考虑 runtime path。

### 2. 优先强类型，不优先字符串拼装

不要散落：
- 字符串拼 cache key
- 字符串拼 route 规则
- 字符串拼 provider 语义

优先抽象成：
- contributor
- definition
- descriptor
- provider

### 3. 优先收口，不优先横向扩展

如果一个模块已经存在主链缺口：
- 先补闭环
- 再加新能力

不要一边保留旧链，一边继续加新模块。

---

## 测试要求

### 1. 测试信号必须和主链一致

测试不是只验证“能跑”，还要表达“框架正式维护哪条路径”。

因此：
- 如果主链已经 AoT 化，就不要继续大量维护 runtime reflection path 测试
- 如果真实闭环已经有 IntegrationTests，就不要再把过期的伪集成测试当主测试资产

### 2. 优先真实集成测试

以下场景优先写真实集成测试：

- 认证链路
- 租户链路
- Dynamic API 主链
- Setting Management
- 权限与上下文联动

Mock 测试可以保留，但不能替代全链路验证。

### 3. 新增测试时的判断标准

如果测试验证的是已经降级的 legacy 路径，需要先问：

- 这条路径还是不是正式主链？
- 这个测试会不会误导后续维护者继续修 legacy 而不是修主链？

如果答案是不该长期维护，就不要继续加强该测试。

---

## 构建与命令

常用命令：

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project samples/LibraryManagement/LibraryManagement.Web
```

---

## 变更前自检

在提交任何方案或代码前，先自问：

1. 这是在强化唯一主链，还是在偷偷保留双轨？
2. 这是在减少反射、提升 AoT，还是在继续依赖 runtime 技术路径？
3. 这是平台能力，还是业务补丁？
4. 这套测试验证的是正式主链，还是过期链路？
5. 这次修改会不会误导后续维护者继续维护 legacy path？

如果第 1、2、4、5 条答案不理想，应先停下来调整设计。

---

## 当前优先级共识

基于当前项目状态，后续开发优先级遵循：

1. 优先收口框架主链
2. 优先 AoT / 生成链
3. 优先平台能力闭环
4. 最后再做横向模块扩展

对已有能力的理解：

- `Tenant Management`：主链已基本闭环，优先维护真实主链与真实测试
- `Setting Management`：已是正式平台能力，后续应复用而不是绕开
- `Dynamic API`：AoT 化主目标已完成，后续重点是彻底收口 legacy runtime 路径
- `Feature Management`：如果要做，应建立在 Setting / Tenant / Dynamic API 主链稳定的前提上

---

## 参考位置

- 文档索引：`docs/INDEX.md`
- 架构文档：`docs/01-architecture/`
- 示例项目：`samples/LibraryManagement/`

---

**最后更新**: 2026-04-12
