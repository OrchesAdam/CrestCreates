# DynamicApi 重构计划（通过 SourceGenerator 实现注入）

## 背景

用户希望 DynamicApiModule 通过 SourceGenerator 实现注入，而不是通过运行时扫描程序集。这与现有的 ModuleSourceGenerator.cs 实现方式一致。

## 目标

1. 创建 DynamicApiSourceGenerator，在编译时扫描带有 [DynamicApi] 特性的接口
2. 生成相应的控制器注册代码
3. 简化 DynamicApiModule，移除运行时扫描逻辑

## 任务 1: 创建 DynamicApiSourceGenerator
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 在 CodeGenerator 项目中创建 DynamicApiSourceGenerator.cs
  - 扫描带有 [DynamicApi] 特性的接口
  - 生成控制器注册代码
  - 参考 ModuleSourceGenerator.cs 的实现方式
- **Success Criteria**:
  - SourceGenerator 能够正确扫描和生成代码
  - 生成的代码能够正确注册控制器
- **Test Requirements**:
  - `programmatic` TR-1.1: 编译成功，无错误
  - `human-judgement` TR-1.2: 生成的代码结构正确
- **Notes**: 需要参考 ModuleSourceGenerator.cs 的模式

## 任务 2: 创建 DynamicApiInfo 数据结构
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**:
  - 定义 DynamicApiInfo 类，用于存储接口信息
  - 包含接口名称、命名空间、方法信息、路由信息等
- **Success Criteria**:
  - DynamicApiInfo 能够正确存储接口信息
  - 便于后续代码生成
- **Test Requirements**:
  - `programmatic` TR-2.1: 编译成功
  - `human-judgement` TR-2.2: 数据结构设计合理
- **Notes**: 参考 ModuleInfo 的设计模式

## 任务 3: 修改 DynamicApiModule，移除运行时扫描逻辑
- **Priority**: P1
- **Depends On**: 任务 1, 任务 2
- **Description**:
  - 移除 OnApplicationInitialization 中的运行时扫描逻辑
  - 简化 DynamicApiModule，只保留配置选项
  - 移除或简化 DynamicApiGenerator（如果生成器逻辑已经在编译时完成）
- **Success Criteria**:
  - DynamicApiModule 不再包含运行时扫描代码
  - 模块结构简化，符合单一职责原则
- **Test Requirements**:
  - `programmatic` TR-3.1: 编译成功
  - `human-judgement` TR-3.2: 模块职责清晰
- **Notes**: 保留 DynamicApiOptions 用于配置

## 任务 4: 更新 DynamicApiGenerator（或移除）
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**:
  - 如果 DynamicApiGenerator 中的逻辑已经在编译时完成，可以考虑移除或简化
  - 如果需要保留运行时功能，只保留必要的部分
- **Success Criteria**:
  - DynamicApiGenerator 功能明确
  - 无重复逻辑
- **Test Requirements**:
  - `programmatic` TR-4.1: 编译成功
  - `human-judgement` TR-4.2: 逻辑无重复
- **Notes**: 确保不丢失必要的功能

## 任务 5: 测试重构后的实现
- **Priority**: P0
- **Depends On**: 任务 4
- **Description**:
  - 编译所有相关项目，确保无编译错误
  - 验证生成的代码能够正确注册控制器
  - 验证 DynamicApi 功能正常工作
- **Success Criteria**:
  - 所有项目能够成功编译
  - 生成的代码正确
  - DynamicApi 功能正常
- **Test Requirements**:
  - `programmatic` TR-5.1: 编译成功
  - `programmatic` TR-5.2: 生成的代码存在且正确
  - `human-judgement` TR-5.3: 功能验证通过
- **Notes**: 确保所有重构后的代码都能正常工作