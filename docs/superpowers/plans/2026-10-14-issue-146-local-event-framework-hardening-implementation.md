# #146 实施计划与设计交接

日期：2026-10-14。基线：master `5d3de8c544cfc888aca23124303494929c4a1c2c`。
详细契约：[Spec rev.1](../specs/2026-10-14-issue-146-local-event-framework-hardening-design.md)。
状态（2026-10-14 更新）：S1 已执行（见 `2026-10-14-issue-146-issues-log.md`）：Spec/计划逐字入库、7 条失败基线运行落证、
typed bridge 三程序集编译实验通过、bootstrap 挂接核对完成。**S2/S3 等待设计审查通过后实施**（S1 最后一项未勾选）。

## S1 — 锁定契约和可复现缺口

- [x] 复核开工时 master，记录 #128 等在途 PR 与共享工具链修改。（#127 已合并、#128 未开工、仅 docs PR #147 在途，无工具链冲突）
- [x] 将 Issue 中 Host A→B 污染、冲突 first-wins、冻结后工厂变化、Transient 两次构造的原始探针转为仓库内行为测试。（前 3 类 H01/H02/P01 已入库并 3/3 失败落证；冻结后工厂变化由 R01/记录侧基线覆盖其记录/重放面）
- [x] 补旧 key 改名、精确治理旧版本、租户恢复和 pre-MarkRetrying 解码/准备的最小失败用例。（G01/C01/C01b/R01 入库并 4/4 失败落证；旧 key 改名按 issues log 偏差说明随 S3 alias API 落地，不制造假基线）
- [x] 编译验证 generated public typed bridge 与 internal execution core 的边界，覆盖引用程序集事件和 handler-only 项目。（/tmp 三程序集实验：编译通过 + `constructs=1 handled=1+1`；handler-only CS0122 负例由 #126 跨程序集测试覆盖）
- [x] 锁定 ContextVersion 历史 null、版本 alias、释放失败与结算 Unknown 的矩阵。（Spec §7.3/§8 已锁定并随 S1 入库）
- [x] 确认 existing event-registry bootstrap 依赖挂接；非 hosted 必须 Built 后才能治理执行。（结论与 Spec §3.3 一致，见 issues log：不得假设启动顺序，未 Built fail-closed 且不缓存为永久失败）
- [ ] 设计审查通过后再实施 S2/S3。

交付：失败基线记录、具体测试清单、经审查 Spec。不要为准备阶段在生产层增加测试专用 public 注入点。

## S2 — Host codec 与单次准备

- [ ] 引入 LocalEventRegistration / LocalEventJsonCodecRegistration；builder 输入与 frozen binding 分离。
- [ ] 修改 JsonContractSourceWriter 与 LocalEventCodeWriter，生成 Host IServiceCollection 贡献；不改变 LocalEvent 既有 JSON 命名策略。
- [ ] 逐项 codec/来源/重复声明校验，集合及 JsonTypeInfo/options 真正锁定。
- [ ] typed Prepare 一次解析全部实际 Handler，required 检查后执行同一数组；借用与拥有 scope 分离。
- [ ] 修改 dispatcher、同步 bus、Channel consumer、recorder、replay；解码/准备移到 MarkRetrying 前。
- [ ] 增 additive 结果事实并把业务执行、结算写入、异步释放拆开；覆盖提交后抛错的故障注入。
- [ ] 归档旧全局 codec / Probe 双解析等死契约，更新全部生成调用点和测试夹具。
- [ ] S2 独立提交时仍保留既有显式 validator 行为；治理/策略切换随 S3 原子迁移，不能在中间提交静默移除检查。

验证：Spec H01–H04、P01–P04、R01–R04（不依赖 S3 的部分），原 LocalEvent/JsonContracts/CodeGenerator 回归。
退出：唯一新生命周期链可独立运行；不留新旧 codec 双轨。

## S3 — 持久身份、策略与上下文

- [ ] ReplayKey / ReplayAlias、同版本约束、payload legacy identity 分工与冲突空间。
- [ ] Governance.Required、TenantMode、显式 validator / 执行 check 激活；公共 prepare 检查覆盖直接 dispatcher。
- [ ] 基于 Built registry 精确关联 Id/version/Type；治理旧行采用映射必须显式且限定历史身份，不升级信任未知记录。
- [ ] context snapshot、Channel work item、原快照失败记录；TenantId/correlation 一致性检查。
- [ ] await tenant provider 解析与同步 Change/Push 分离；先激活再构造租户敏感依赖。
- [ ] ICurrentTenant.Change nullable host 语义与 LIFO/await/异常恢复；各层清理异常不跳过其他恢复。
- [ ] LocalContextVersion additive 可空列、双次可重复升级脚本、EF/InMemory 字段往返。
- [ ] 历史 null 默认拒绝；显式历史 host policy 不得覆盖 Required。
- [ ] 手工/后台重放不再 Publish、不新增 DLQ；状态与预算保持原行权威。

验证：Spec I01–I03、G01–G05、C01–C05、D01–D02，补全 R 系列。
退出：新旧持久记录真实可重放或按契约拒绝，租户/治理/结算失败均有确定结果。

## S4 — 证据、迁移与最后验收

- [ ] 更新 existing LocalEvent NativeAOT fixture 的分场景断言及 sentinel。
- [ ] 在既有 Local.Tests.Integration 补 EF Core + Npgsql + PostgreSQL fixture（每测试独立 schema），从旧 schema 升级两次并测试 fresh-scope store，证明不是 EF InMemory / EnsureCreated 的空库模型测试；其他 Provider 单列未验证。
- [ ] 重跑受影响 RabbitMQ unit / broker / trim / native，保留其独立 codec 格式与私有 validator 边界。
- [ ] 跑工具链全量回归及依赖边界，检查 clean/incremental/删除声明/跨项目正确性。
- [ ] 更新 memory.md、#123 evidence matrix、迁移文档、Issue/PR 当前 head 和计数。
- [ ] 按实际组合声明 NativeAOT/trim；EF、生产 tenant provider、授权/审计持久化未覆盖项单列。
- [ ] 设计/实现审查全部闭环后提交合并建议，不自动合并、不恢复 CI。

## 验证命令

以下是实施交接命令，**本轮未执行**。在实际实现 checkout 中运行，按需要提供本地 broker/数据库；不得运行保留的 live 资产容器。网络受限时可仅在验证命令添加 NuGetAudit=false，不修改工程默认配置。

```bash
dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.Tests/CrestCreates.EventBus.Tests.csproj
dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj
dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/CrestCreates.JsonContracts.BuildTasks.Tests.csproj
dotnet test tests/Framework/Ddd/CrestCreates.Application.Tests/CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~Tenants"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj
dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.Local.AotHost.Tests/CrestCreates.EventBus.Local.AotHost.Tests.csproj
dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.Tests/CrestCreates.EventBus.RabbitMQ.Tests.csproj
dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.AotHost.Tests/CrestCreates.EventBus.RabbitMQ.AotHost.Tests.csproj
```

原生 fixture 由门禁执行实际 publish/link/run。schema 升级与 tenant provider 生产组合证据必须另记，不从这些命令中的其他组合推导。上游编译/依赖变化造成失败时，区分环境问题、已有失败与本次回归，不能靠削弱测试或更改主链绕过。

## PR 组织与交接

- 可以单 PR 分 S1–S4 提交；也可 S2 一个独立可审查 PR、S3/S4 后续 PR。不要长期叠放不可独立运行的半成品。
- S1 Spec 审查未通过前不实施尚未明确的治理/上下文行为；用户已有明确授权的范围内正常完成设计和可审查产物。
- S2 与 S3 有公共接口顺序依赖；共享 CodeGenerator / JsonContracts 修改串行合并。最多两个实现 PR 在途。
- 旧文件移动到 99_RecycleBin/issue-146-local-event-hardening，由人工后续决定清理；不直接删除。
- 不指定负责人/截止日期，不创建额外 Issue、不派发 Agent，不自动触发开发。

