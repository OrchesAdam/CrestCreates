# #129 实施计划与设计交接

状态：设计已准备，功能未实施。设计基线 `196b6f66048ef32d644eeb856d00d58ae9d34965`；规范见 `docs/superpowers/specs/2026-10-17-issue-129-generated-http-controller-exit-design.md`。开工前重新核对 master、进行中的 Tooling/Web PR 和 #128 交接，保持串行。

详细设计全文已发布到 [#129 评论](https://github.com/OrchesAdam/CrestCreates/issues/129#issuecomment-6228947951)，发布后回读核对与本地 Spec 原文一致。

## S1 — 调用图、行为基线与设计冻结

- [ ] 编译真实 sample，盘点 authored/生成源码/Task/registry/Host/route/runtime helper；记录无法运行时的确切前提，不用文档注释替代执行事实。
- [ ] 冻结 Ticket 11 动作矩阵；分别记录三个 GET 写动作的兼容语义、Ticket.Update 权限收紧、body/stamp/错误响应变化。
- [ ] 以真实源码引用复现手写服务的组合信封同轮不可见、全局激活、失败变 500，以及实际并发/租户缺口；负例的其他设施全部满足。
- [ ] 冻结新 Attribute/model/manifest/诊断、failure bridge、error metadata 所有者及项目依赖图；原规范与本次设计的名字是拟定 API，不作为已存在能力。
- [ ] 输出具体 legacy consumer/owner 允许清单与归档替代表，审查通过后进入 S2。

## S2 — 通用 HTTP 投影与 JSON

- [ ] 提取并 source-link shared model/emitter；CRUD 与手写 service 前端消费同一 HTTP 后端。
- [ ] 扩展前置工具链 ordinary Compile、完整 ownership/provenance、显式 DependsOn、诊断与错误状态；复用现有 stamp/transport，不复制实现。
- [ ] 自动 body/input/scalar/list/typed result/void/error contracts；官方 STJ 单 driver/真实 MSBuild 可见。
- [ ] AddGeneratedAppServiceHttp 显式贡献/handler/JSON 装配，业务 DI owner 不重复；转换已有 Asset/Procurement 等正式 compatibility consumer。
- [ ] B01–04/H08、生命周期与外部 packaged consumer 验收通过。

## S3 — Ticket 与真实 Host

- [ ] ContractType 限定 ITicketAppService；完整路由/Kind/权限声明，继承基类未声明方法不暴露。
- [ ] 六个写动作接唯一 UoW，11 动作 CT 传递；真实 typed validation 和 stamp 语义。
- [ ] Runtime 中立失败事实 + Web adapter 复用现有异常转换/错误 JSON 权威；未知错误脱敏，不改成 sample 私有 catch-switch。
- [ ] Host 正式 capability 装配，既有认证/租户/审计保持；补双租户生产选库 factory，不能用 shared connection 证明隔离。
- [ ] H01–08 真实 PostgreSQL/认证/授权/租户/失败/取消/UoW 验收通过。

## S4 — Legacy 隔离与归档

- [ ] 仍有消费者的旧 Minimal API runtime/analyzer/adapter 显式隔离、单向依赖、有限允许清单；无 provider 时无自动 fallback。
- [ ] TicketApi、Controller 两生成器、GeneratedApiController/ApiOverride 分支、旧 CRUD stub bundle、死反射 route helpers 及专用旧测试移到回收站。
- [ ] scalar helper 留在实际消费者所属边界；保留正式 CrudServiceSourceGenerator 诊断入口；旧 warning 随真实旧实现退出移除。
- [ ] 更新默认/legacy analyzer 分发、Compile/package globs、SDK 配置，避免归档代码继续生效或新 analyzer 循环依赖。
- [ ] L01/L02 的依赖、语义符号、运行路由与变异守卫通过。

## S5 — Native、回归与交接

- [ ] Ticket native fixture 使用真实服务/DTO/正式投影，持久化叶子用诚实声明的静态内存 provider；不换成等价假业务服务。
- [ ] publish/link/run、loopback、逐场景/总 sentinel、真实 metadata 拒绝和变异、进程回收证据。
- [ ] 复跑 #128、CapabilityEndpoint 与受影响 LocalEvent/RabbitMQ/JSON/package/边界门禁；按最终提交留下实际计数。
- [ ] 同步 memory/AGENTS/#123 evidence matrix/migration/归档说明/支持边界，发布复审入口。
- [ ] 本任务不自动合并、不恢复 CI；设计发布不代表任一实现切片已完成。
