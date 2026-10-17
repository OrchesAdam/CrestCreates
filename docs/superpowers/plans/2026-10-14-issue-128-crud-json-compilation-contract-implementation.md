# #128 实施计划与设计交接状态：设计交接，未启动实现。基线 de50b33765fbdf8992c77ff15a126147c4cfeb7a。规范见同日期 issue-128-crud-json-compilation-contract-design.md。下列新增类型/路径是待实施产物，不表示今天已经存在。

## S1 — 锁定真实失败与设计审查

- [ ] 以开工时最新 master 复核基线；记录任何在途 CodeGenerator/JsonContracts PR，再串行合并/重验。
- [ ] 盘点 GenerateCrudService/GenerateEntity 的真实定义和消费者；提交真实程序集引用的编译探针，不能把 helper stub 当公共 API。锁定 GenerateCrudService(typeof(Entity)) partial service 入口与字段/路由/权限表。
- [ ] 以真实 MSBuild/官方 STJ 复现 DTO 同轮不可见；补 mapper/compatibility input/closed response envelope 负例。generator 测试增加一个同 driver 的正式方法；旧串行 helper 不再作为这条链的验收。
- [ ] 补请求、响应、带 Filter/Sort 的查询三类独立基线；负例只缺待验证能力，身份/权限/UoW/repository 等其他设施满足前提，防止假绿。
- [ ] 提交真实 Attribute + CrudGenerationModel 的可编译设计样例，记录 owner、namespace、引用实体/同短名、构造/mapping、full identity、URI/descriptor collision 策略。
- [ ] 固定 CC CRUD 诊断族登记和 target/manifest schema；设计审查通过后推进 S2/S3。基线红色必须在 PR 描述按用例解释，不能算回归通过。

主要落点：
- Domain.Shared/Attributes/GenerateCrudServiceAttribute.cs（新增）。
- CodeGenerator/CrudServiceGenerator 的模型/纯 emitter 提取。
- tests/Tooling/CrestCreates.CodeGenerator.Tests/CrudServiceGenerator 与新的真实 build contract 用例。

## S2 — BuildTasks 物化与 JSON 契约

- [ ] 在 CrestCreates.BuildTasks 增 GenerateCrestCrudArtifacts task 与标准 Common/Repository/Package props/targets；Roslyn 执行复用 JsonContracts.Tool 的隔离进程协议，prepare-crud 与 JSON generate 是不同命令/阶段。不能让 MSBuild task 动态扫描应用 DLL。
- [ ] 一份 shared-source CrudGenerationModel/emitters，前阶段输出 DTO、interface、permission、mapping seeds、AppService、typed query adapter、compatibility artifacts、显式 activation 与 model/provenance manifest。不得复制两套 DTO/映射/投影规则。
- [ ] 一个 CRUD bundle + manifest/stamp；AdditionalFiles 与 compiler-visible metadata 共同核对输出版本/ownership；正式声明缺前阶段给 Error。
- [ ] IncludeCrestCrudArtifacts 显式接入 CrestCreatesJsonContractGenerationDependsOn，早于 JSON 收集 Compile；正确排除自身历史输出与 source-generator obj 树，去重 Compile。
- [ ] EntitySourceGenerator 对被本 manifest 拥有的 DTO/mapping seeds 停止输出；CrudServiceSourceGenerator、AppServiceCompatibilityGenerator、ServiceRegistrationGenerator 对已拥有产物只验证/跳过。保留普通 Entity/手写 service 的不相关行为。
- [ ] JsonContracts 新增 CRUD surface/provenance/closure/root writer，使用现有 context/normalizer；official STJ 消费普通源码。包含 capability input envelopes、typed payload/page envelopes、void/404、有限 filter converter。
- [ ] 实现内容 fingerprint、完整输入列表、no-op write-if-changed、声明移除/改名、missing-output repair、失败无成功 stamp、独立 design-time 与 TFM/RID 输出；IDE 的 CRUD context/root 声明使用同一 JSON 模型，不能引用未生成的 Context.Default。CodeGeneration=false 但声明仍在时确定报错，移除声明时不保留旧输出。
- [ ] 包消费从空外部目录首次 build；Repository/Package transport 互斥，Release tool 路径和 shared source/emitter 版本匹配；Tooling 不进入运行时 publish。

退出：D01–04、J01–02、B01–03 的对应 build 契约通过；真实单 driver 的 STJ/mapping 成功，不能靠先跑 Crud SG 再把产物喂给下一 SG。

## S3 — typed HTTP 与执行行为

- [ ] 复用 AppServiceCompatibility 的模型/emitters，CRUD registration mode 固定 ExplicitHost：descriptor providers 进入 DI 激活集合，不再通过旧 CRUD DynamicApiGeneratedProvider 或无条件全局 descriptor ModuleInitializer 激活。
- [ ] 每程序集 AddGeneratedCrud 显式注册 typed services/contributors，幂等；DI 只生成一份；interface/concrete 同 scoped instance。
- [ ] MapCrestCapabilityEndpoints 的权威 registry Build 汇总加入本 Host 已激活的 typed providers；未 Built 一次构建，已 Built 验证必需 identities，缺失明确拒绝。不能增量改 frozen registry，不能为 CRUD 建另一个 dispatcher。ready 仅在构建成功后提交，失败不永久缓存；每动作 typed result mapper 缺失时前置拒绝，不走通用 output fallback。
- [ ] input binder 复用 typed body reader，按本设计 required 语义；Update/Delete envelope 及 CT 真正传到 Capability/AppService/UoW 唯一内核。
- [ ] result emitter 对 ctx.Output 按静态返回类型转换；CompatibilityHttpResultMapper 新增无 fallback 的 typed overload，并与既有 envelope 构造共享。正式 CRUD 不使用 DynamicApiResponse<object>。
- [ ] source-generated JSON contributor 参与最终 Http.Json.JsonOptions；按 Host 解析/冻结 metadata，校验 body/input/result/void/error 完整集合，不信任默认 reflection resolver 补洞。
- [ ] FilterDescriptor 的有限 converter/DOM 值域明确支持；generated field adapter 静态转换/过滤/排序/分页，替换该 CRUD 消费点的反射 QueryExecutor，包括默认排序。非法字段/运算/类型确定拒绝。
- [ ] 真实 JIT HTTP 集成覆盖五动作、wire contract、并发、错误/取消、UoW 提交与响应失败的独立事实；复用实际身份/权限/tenant 设施，业务不增加手写基础设施代码。

重点落点：
- AppServiceCompatibilityGenerator 的 models/endpoint/invoker/result emitters（共享部分）。
- DynamicApi 的 CapabilityEndpointBodyReader/JsonContractValidator/registry bootstrap/result mapper/CompatibilityHttpResultMapper。
- Application.Contracts 的 FilterDescriptor 有限 wire 契约；新生成 query adapter，不广泛重写其他 QueryExecutor 消费者。
- UoW 只消费既有 #137 public contract，不另建 bool 执行路径。

退出：J/H/Q 对应真实运行用例通过；正式 CRUD 的 legacy JSON/provider 消费清零。

## S4 — native、迁移与交接

锁定新增 fixture 名称：
- tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Domain
- tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Feature
- tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture（Web Host）
- tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Tests

- [ ] Domain → Feature → Host 与 packaged Feature → 外部 Host；fixture 不手写 DTO、等价 service/mapper/context 根表。
- [ ] memory repository/UoW 明确能力且走真实 generated service/typed query/Capability 执行；保留正式调用，不拿移除 middleware 或假服务冒充 JSON 主链验证。
- [ ] 原生发布/链接/运行 + loopback 五动作、非空 filters/sorts/default sort/page、复杂 scalar、输入拒绝/metadata 拒绝；分场景 sentinel + 总 sentinel。
- [ ] 对 fixture 构造坏预期/漏 root/错误 typed envelope 等变异，确认目标断言失败且进程回收。缺 metadata 负例维持真实 descriptor/binding 激活，不允许零端点假绿。
- [ ] 受影响 CodeGenerator/BuildTasks/JsonContracts build+package、LocalEvent native、RabbitMQ unit/broker/trim/native、既有 CapabilityEndpoint 原生 HTTP、UoW 回归；按最终 head 留证。
- [ ] 归档旧 CRUD provider/emitter/失效测试后才移除 #warning。新增架构守卫；保留仍有消费者的隔离 legacy，不提前替 #129 删除。
- [ ] 更新 AGENTS 的真实 Attribute、memory/#123 evidence matrix/迁移指南/Issue 与 PR head/计数；交给 #129 的内容包括 wire shape、typed JSON roots、投影与 activation API、已验证组合/限制。
- [ ] 实现审查闭环后给合并建议，不自动合并，不恢复 CI，不自动派发 Agent。

## 验证命令交接

以下是待实施后的命令，**本设计轮未执行新验收**。网络受限时只在命令行加 NuGetAudit=false，不改变项目默认设置。

~~~bash
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj
dotnet test tests/Tooling/CrestCreates.BuildTasks.Tests/CrestCreates.BuildTasks.Tests.csproj
dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/CrestCreates.JsonContracts.BuildTasks.Tests.csproj
dotnet test tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests/CrestCreates.JsonContracts.Build.PackageTests.csproj
dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests/CrestCreates.DynamicApi.Tests.csproj
dotnet test tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Tests/CrestCreates.GeneratedCrud.AotFixture.Tests.csproj --filter Category=NativeAotGate
dotnet test tests/Framework/Api/CrestCreates.CapabilityEndpoint.AotFixture.Tests/CrestCreates.CapabilityEndpoint.AotFixture.Tests.csproj --filter Category=NativeAotGate
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj
~~~

consumer matrix 必须另建全新临时目录执行 build/rebuild、源码变化/排除、reference 更新、DesignTimeBuild 与 package layout；仅 dotnet test 的 unit suite 不替代这些构建实证。原生 fixture 的 harness 明确调用 publish -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot，并请求发布后的真实进程。

## 实施纪律

- 不修改已审查的 LocalEvent/RabbitMQ wire、生命周期或 codec 格式；共享工具链变化重跑它们的独立门禁。
- 每轮修复补能击中缺口的实际回归，核对当前 head；不只删断言/压 warning。
- 没有 NativeAOT publish/link/run 的组合写未验证；EF/JIT/内存分别声明。
- 实施中发现必须改变 public 属性、wire 或 registry 装配契约，应先修订该 Spec 并给出影响/迁移，不以 issues log 一条偏差替代正式设计。


