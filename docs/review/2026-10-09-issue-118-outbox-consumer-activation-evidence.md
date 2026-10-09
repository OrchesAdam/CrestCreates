# Issue #118 — Generated Outbox Consumer Activation Evidence

- 日期：2026-10-09
- Issue：[C88-01 / #118](https://github.com/OrchesAdam/CrestCreates/issues/118)
- PR：[#119](https://github.com/OrchesAdam/CrestCreates/pull/119)
- Spec：[2026-10-09-issue-118-generated-outbox-consumer-activation-design.md](2026-10-09-issue-118-generated-outbox-consumer-activation-design.md)
- 审查报告：[2026-10-09-pr-119-generated-consumer-activation-review.md](2026-10-09-pr-119-generated-consumer-activation-review.md)

## 1. 变更摘要

### PR #119 head 1（原始实现）

- 新增 `GenerateOutboxConsumerActivationAttribute` marker 和 `IOutboxConsumerActivation<TSelf>` static abstract 接口
- 新增 `OutboxConsumerActivationSourceGenerator`（IIncrementalGenerator）
- 修改 `AddOutboxRequiredConsumer` 使用 static factory
- 三处生产 consumer 添加 marker + partial
- 移除两个 Host 中的 `Replace` 工厂

### PR #119 head 2（审查修复）

修复审查报告 R1–R4：

| 修复 | 文件 | 变更 |
| --- | --- | --- |
| R1 | `OutboxConsumerActivationSourceGenerator.cs` | hintName 使用 fully-qualified type identity 编码；validModels 按 symbol identity 去重 |
| R2 | `OutboxConsumerActivationSourceGenerator.cs` | 生成 partial 的 base list 包含 `IOutboxConsumerActivation<T>`；不再要求业务手写接口 |
| R3 | `OutboxConsumerActivationSourceGenerator.cs` | 使用精确 metadata name 匹配 attribute；keyed attribute 使用精确身份 |
| R4 | `OutboxConsumerActivationSourceGenerator.cs` | 使用 `IsGlobalNamespace` 判断；全局命名空间不输出 namespace 声明 |

## 2. 测试矩阵

### 2.1 生成器回归测试（18 项全部通过）

| 测试 | 覆盖 | 结果 |
| --- | --- | --- |
| `R1_TwoNamespacesSameTypeName_Should_ProduceTwoDistinctOutputsAndCompile` | R1 去重 + 无碰撞 hintName | PASS |
| `R1_TwoAttributedPartialsOfSameType_Should_ProduceOneOutputAndCompile` | R1 partial 去重 | PASS |
| `R2_MarkerOnlyConsumer_Should_GenerateActivationInterfaceAndCompile` | R2 不要求手写接口 | PASS |
| `R2_GeneratedOutput_Should_ContainExplicitInterfaceImplementation` | R2 生成接口实现 | PASS |
| `R3_ExternalSameNamedAttribute_Should_ProduceZeroOutputAndZeroDiagnostics` | R3 精确匹配 | PASS |
| `R4_GlobalNamespaceConsumer_Should_CompileWithoutNamespaceSyntaxError` | R4 全局命名空间 | PASS |
| `Positive_ZeroParameterConstructor_Should_GenerateParameterlessNew` | 零参数正向 | PASS |
| `Positive_MultipleDependencies_Should_GenerateGetRequiredServiceForEach` | 多依赖正向 | PASS |
| `Positive_InternalConsumer_Should_GenerateInternalPartial` | internal 可见性 | PASS |
| `CCOCA001_NonPartialClass_Should_ReportError` | 诊断 001 | PASS |
| `CCOCA001_RecordType_Should_ReportError` | 诊断 001 record | PASS |
| `CCOCA001_AbstractClass_Should_ReportError` | 诊断 001 abstract | PASS |
| `CCOCA002_DoesNotImplementRequiredConsumer_Should_ReportError` | 诊断 002 | PASS |
| `CCOCA003_MultiplePublicConstructors_Should_ReportError` | 诊断 003 | PASS |
| `CCOCA004_IServiceProviderParameter_Should_ReportError` | 诊断 004 | PASS |
| `CCOCA004_IServiceScopeFactoryParameter_Should_ReportError` | 诊断 004 scope factory | PASS |
| `CCOCA006_HandWrittenCreateOutboxConsumer_Should_ReportError` | 诊断 006 | PASS |
| `NoMarker_Should_ProduceZeroOutput` | 无 marker 零输出 | PASS |

完整 CodeGenerator 测试套件：301 项全部通过（283 既有 + 18 新增）。

### 2.2 运行命令

```bash
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
```

## 3. 兼容性说明

本次变更为 source/binary compatibility 变更：

- 所有使用 `AddOutboxRequiredConsumer` 的程序集需重编译
- Consumer 类型需添加 `[GenerateOutboxConsumerActivation]` 和 `partial`
- 不再需要 Host 中手写 consumer 工厂
- 关闭 code generation 的构建将按 static contract 缺失编译失败

## 4. 迁移说明

### 已完成

1. 三个生产 consumer 添加 marker + partial：
   - `WorkflowContinuationOutboxConsumer`
   - `AssetMaintenanceDecisionConsumer`
   - `ProcurementHumanTaskDecisionHandler`
2. `AddOutboxRequiredConsumer` 使用 static factory
3. 两个 Host 移除 `Replace` 工厂

### 不需要

- 不保留旧反射 overload
- 不保留运行时 fallback
- 不保留双轨运行开关

## 5. 已知限制

审查报告指出的额外诊断缺口（required-member、abstract 拦截、IServiceScopeFactory 参数）已部分覆盖：

- abstract 类型：已添加 CCOCA001 拦截
- IServiceScopeFactory 参数：已添加 CCOCA004 拦截
- required-member：生成器检测 `RequiredMemberAttribute`，但测试未覆盖

完整诊断矩阵的逐条覆盖应在后续迭代中完成，不以三个样例可以编译代替完整公共契约。

## 6. 待完成

- Runtime Delivery 契约测试（真实 DI + canonical 注册）
- 负向 native fixture
- 两个 golden native gate 验证
- 完整 RED/GREEN evidence（精确 head SHA、退出码、日志）
