# #128 S2 整体审查、②③实施与验证

审查基线：`cf44e947410b7b96a66cd56f7f2866b9bacd74e0`。
实现提交：`2580129438a31af4ed2557595d3c9ce56e09b0e9`。后续交付提交只补审查/证据文档。

**结果：①的逐产物 FQN ownership 跳过方向成立；审查发现已修复，② CRUD 生命周期矩阵与③跨进程两阶段 driver 已完成。** 原有两个输入/响应 roots driver 红例已转绿；S3 的 object-result 及两条查询归一红例保留。未实施 S3 typed HTTP/查询切换或 S4 GeneratedCrud 原生 fixture，不把这次构建链验证称为完整 CRUD 运行链交付。

## ① 审查结果及修复

基线 CodeGenerator 独立复跑为 416 pass + 3 既有 red / 419。manifest 中 type FQN 与 owner 分离、Entity/Service 逐项跳过、无 manifest 时仍生成的双向控制均成立。下列问题来自实现审读及随后落地的回归控制；没有把所有新增控制伪称为先红后绿。

| 发现 | 修复及证据 |
| --- | --- |
| 成功 stamp 只检查 bundle 缺失，outputs manifest 缺失/被改动时仍可复用旧状态 | 新增 CrestCrudGenerationState，绑定 input manifest/bundle/outputs manifest 内容 hash；任一输出缺失或被改动即重新生成。缺 bundle、缺 manifest、改 owner 后自愈控制均通过。 |
| design-time 跳过生成，却可能引用正常构建的旧 bundle/JSON 文件 | CRUD 与 JSON 都在独立 design-time 分区生成当前输入；IDE Compile/SkipCompilerExecution 控制证明正常输出和 stamp 不被覆盖。 |
| 禁用生成的最终值、RID、task 实现不完整地参与输入身份 | fingerprint 增加最终 ValidateOnly、RID、task assembly hash，CLI 验证模型 schema 版本；禁用生成/未知 schema 确定失败且不记新成功状态，恢复配置后构建成功。 |
| 输出路径只按字符串前缀判定，且 input/stamp 等覆盖路径未完整校验 | 五类路径统一采用相对 SDK intermediate root 的边界校验；同前缀兄弟目录全部拒绝 CCCRUD006。 |
| 布尔工具链 marker 可作为装配完成的唯一证据 | 正式 analyzer 还核对 manifest 中的具体 type ownership 及当前 compilation 的物化类型；单独伪造 ready marker 的真实工程以 CCCRUD002 拒绝。 |
| JSON 与 CRUD manifest 仅做单向 owner 对账，缺 schema/type/hash 完整校验 | 按 schema、owner assembly/service、完整 hint/type 集合、artifact hash 双向核对；缺失或不匹配报告 CCCRUD008。框架 envelope 必须唯一来自引用程序集，源内 shadow/歧义报告 CCCRUD003。六类 CLI 协议负例通过。 |
| ownership reader 接受文件名后缀；非空 ImmutableSortedSet 无值相等，确定序不足以稳定增量值 | 精确文件名与支持 schema 才声明归属；三条管线使用内容 comparer。异常仍落共享空集，不新增 analyzer 运行期依赖。相应边界/比较控制通过。 |

附带修复已记录的 PackageTests locale 问题：以 CS0122 替代英文 inaccessible 文本断言。非英文 locale 的完整 PackageTests 29/29 通过。

## ② CRUD 专属真实构建矩阵

CrudBuildLifecycleTests 覆盖 17 个用例：

- 干净构建、no-op 内容/时间戳保持、clean 后重新生成。
- bundle/manifest 缺失自愈，manifest 内容改动后重新对账。
- 保留源文件时间戳的内容修改、源文件改名、移除正式声明后清除全部 owner/DTO/JSON roots。
- 禁用生成、未知模型 schema、伪造 ready marker；失败不写新成功状态。
- 首次与后续 design-time 构建生成当前的隔离 surface/roots，随后正常构建消费新源码。
- net10.0/net10.0-windows 与 linux-x64/linux-arm64 的构建产物分区及 RID 指纹。
- bundle/input/output/stamp/temp 五种路径覆盖的同前缀越界拒绝。
- 全新外部目录、独立包缓存、local-feed packaged toolchain 首次构建，断言正式 JSON context 与官方 STJ metadata 存在。

外部消费工程通过复制应用 binary references 提供真实依赖，不导入仓库 Directory.Build 配置。TFM/RID 采用独立 repository-transport 工程，避免仓库全局 analyzer ProjectReference 的既有框架传播行为干扰工具链验收。这里的 Windows/arm64 结果只是构建隔离证据，不是这些平台的原生执行声明。

首轮控制中修正了三处测试设施：IDE 使用 Compile 而非跳过编译却仍要求复制 DLL 的 Build；外部工程显式保存官方生成源；多 TFM 工程清除仓库默认单一 TargetFramework。没有用放宽能力断言掩盖失败。

## ③ 跨进程两阶段 driver

CrudPrecompileProducer 依次调用生产 Tool 的 prepare-crud 与 generate CLI。CRUD bundle 和 JSON context 都成为普通 syntax/Compile 输入，然后官方 STJ 与 ObjectMapping generator 在同一个 driver 中消费。保留最终 compilation 和完整 producer trees，继续执行 S1 的正式类型/owner/成员/root 检查。

测试项目只 build-reference Tool 依赖，不把含 source-linked 模型的 Core 与 analyzer 一起作为运行时程序集引用，避免 CS0433。工具路径按当前 Debug/Release 配置选择；driver 与生产工具保持 nullable=enable，防止 fixture 自己制造 mapper 类型注解不兼容。Debug 完整套件与 Release 三个关键用例均通过。

## 独立验证

| 验证 | 结果 |
| --- | --- |
| 基线 cf44e947 CodeGenerator | 416 pass + 3 red / 419 |
| 修复后的定向矩阵 | 39/39 pass |
| CodeGenerator 全套 | **444 pass + 1 S3 约定 red / 445，0 skipped** |
| JsonContracts 单元 | **131/131** |
| JsonContracts PackageTests | **29/29，zh-Hans locale** |
| DynamicApi | **73 pass + 2 S3 查询 red / 75，0 skipped** |
| Release 两阶段 driver | **3/3** |
| LocalEvent NativeAOT publish/link/run | **1/1，linux-x64，11 场景 + sentinel** |

CodeGenerator 唯一保留的 red：Compatibility_result_contract_must_not_wrap_through_object_typed_output。DynamicApi 的两条 red 仍在 FilterDescriptor.Value 因 JsonElement 抛 ArgumentException。没有更名、skip 或改变这些 S3 契约的目标。

LocalEvent native evidence 的 sha 精确为实现提交 2580129438a31af4ed2557595d3c9ce56e09b0e9，publish/run exit code 均为 0，11 个场景 PASS，无 IL2026/IL3050。该证据只证明共享工具链未破坏现有 LocalEvent 原生主链，不扩展为 GeneratedCrud、ORM Provider 或其他 RID 的原生支持。

## 交接

②③及本轮审查修复已交付。下一步按原计划推进 S3：typed result/HTTP binder/显式激活与有限 query 值域、静态 adapter；随后 S4 执行 GeneratedCrud 原生 fixture、迁移及完整支持声明。当前不合并，不恢复 CI。

全部实现位于隔离工作树 /tmp/crest128-s2-completion。原开发工作目录在实施期间未被编辑。没有创建新的 PR 或派发其他 agent。

主要日志：

- /tmp/crest128-s2-before-codegen.log
- /tmp/crest128-s2-completion-targeted.log
- /tmp/crest128-s2-final-codegen.log
- /tmp/crest128-s2-final-json-unit.log
- /tmp/crest128-s2-final-package.log
- /tmp/crest128-s2-final-dynamic.log
- /tmp/crest128-s2-release-driver.log
- /tmp/crest128-s2-localevent-native.log

原生结构化证据：tests/artifacts/aot-native-evidence/local-event-typed-replay.json（构建产物，不作为源码提交）。后续文档提交不改变已验证实现。
