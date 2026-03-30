# ModuleGenerator 测试计划

## 项目概述

ModuleGenerator 是一个使用 Source Generator 实现的模块化系统，类似于 ABP Framework 的模块化功能。它通过代码生成来实现模块的自动注册和依赖管理，而不是使用反射扫描程序集的方式。

## 测试目标

1. **验证核心功能**：确保 ModuleGenerator 能够正确识别和处理带有 `[Module]` 属性的类
2. **测试依赖管理**：验证模块间的依赖关系能够正确处理，包括拓扑排序
3. **测试代码生成**：确保生成的代码能够正确注册模块并管理其生命周期
4. **测试生命周期钩子**：验证模块的各个生命周期方法能够按正确顺序执行
5. **测试与 ABP Framework 类似的功能**：确保实现的功能与 ABP Framework 的模块化设计理念一致

## 测试任务

### [ ] 任务 1: 核心功能测试
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 测试 ModuleGenerator 能否正确识别带有 `[Module]` 属性的类
  - 测试生成的代码是否包含所有必要的模块信息
  - 测试模块的基本属性（名称、描述、版本）是否正确
- **Success Criteria**:
  - ModuleGenerator 能够正确识别所有带有 `[Module]` 属性的类
  - 生成的代码包含正确的模块注册逻辑
  - 模块的基本属性能够正确设置和访问
- **Test Requirements**:
  - `programmatic` TR-1.1: 验证生成的 `AutoModuleRegistration` 类包含所有模块
  - `programmatic` TR-1.2: 验证模块的基本属性能够正确设置
  - `human-judgement` TR-1.3: 代码生成逻辑清晰，符合设计要求

### [ ] 任务 2: 依赖管理测试
- **Priority**: P0
- **Depends On**: Task 1
- **Description**:
  - 测试模块间的依赖关系处理
  - 测试拓扑排序算法是否正确
  - 测试循环依赖检测
- **Success Criteria**:
  - 模块依赖能够正确解析
  - 拓扑排序结果符合预期
  - 循环依赖能够被正确检测并报错
- **Test Requirements**:
  - `programmatic` TR-2.1: 验证模块加载顺序符合依赖关系
  - `programmatic` TR-2.2: 验证循环依赖检测功能
  - `human-judgement` TR-2.3: 依赖解析逻辑正确，符合预期

### [ ] 任务 3: 代码生成测试
- **Priority**: P0
- **Depends On**: Task 1, Task 2
- **Description**:
  - 测试生成的代码是否能够正确注册模块
  - 测试生成的代码是否包含所有必要的生命周期方法调用
  - 测试生成的代码是否符合 C# 语法规范
- **Success Criteria**:
  - 生成的代码能够正确编译
  - 生成的代码能够正确注册所有模块
  - 生成的代码包含所有必要的生命周期方法调用
- **Test Requirements**:
  - `programmatic` TR-3.1: 验证生成的代码能够正确编译
  - `programmatic` TR-3.2: 验证生成的代码包含所有必要的方法调用
  - `human-judgement` TR-3.3: 生成的代码结构清晰，易于理解

### [ ] 任务 4: 生命周期钩子测试
- **Priority**: P1
- **Depends On**: Task 3
- **Description**:
  - 测试模块的各个生命周期方法是否按正确顺序执行
  - 测试生命周期方法的参数是否正确传递
  - 测试生命周期方法的异常处理
- **Success Criteria**:
  - 生命周期方法按正确顺序执行（PreInitialize → Initialize → PostInitialize → ConfigureServices → ApplicationInitialization）
  - 生命周期方法的参数正确传递
  - 生命周期方法的异常能够正确处理
- **Test Requirements**:
  - `programmatic` TR-4.1: 验证生命周期方法按正确顺序执行
  - `programmatic` TR-4.2: 验证生命周期方法的参数正确传递
  - `human-judgement` TR-4.3: 生命周期执行逻辑清晰，符合设计要求

### [ ] 任务 5: 与 ABP Framework 类似功能测试
- **Priority**: P1
- **Depends On**: Task 4
- **Description**:
  - 测试模块化系统是否具备与 ABP Framework 类似的功能
  - 测试模块的自动注册和依赖管理
  - 测试模块的配置和初始化
- **Success Criteria**:
  - 模块化系统具备与 ABP Framework 类似的核心功能
  - 模块能够自动注册和管理依赖
  - 模块的配置和初始化能够正常工作
- **Test Requirements**:
  - `programmatic` TR-5.1: 验证模块能够自动注册
  - `programmatic` TR-5.2: 验证模块依赖能够正确管理
  - `human-judgement` TR-5.3: 模块化系统的设计理念与 ABP Framework 一致

### [ ] 任务 6: 性能测试
- **Priority**: P2
- **Depends On**: Task 5
- **Description**:
  - 测试 ModuleGenerator 的代码生成性能
  - 测试模块初始化的性能
  - 测试与反射扫描方式的性能对比
- **Success Criteria**:
  - 代码生成性能满足要求
  - 模块初始化性能满足要求
  - 相比反射扫描方式有性能优势
- **Test Requirements**:
  - `programmatic` TR-6.1: 验证代码生成时间在合理范围内
  - `programmatic` TR-6.2: 验证模块初始化时间在合理范围内
  - `human-judgement` TR-6.3: 性能表现符合预期

## 测试环境

- **IDE**: Visual Studio 2022
- **.NET 版本**: .NET 8.0
- **测试框架**: xUnit
- **项目结构**:
  - 测试项目: `CrestCreates.CodeGenerator.Tests`
  - 代码生成器: `CrestCreates.CodeGenerator`
  - 模块化基础设施: `CrestCreates.Infrastructure.Modularity`

## 测试策略

1. **单元测试**：测试 ModuleGenerator 的核心功能
2. **集成测试**：测试模块的注册和初始化过程
3. **性能测试**：测试代码生成和模块初始化的性能
4. **手动测试**：验证生成的代码结构和功能

## 预期成果

- 所有测试任务通过
- ModuleGenerator 能够正确生成代码
- 模块化系统能够正常工作
- 性能满足要求
- 功能与 ABP Framework 类似

## 风险评估

1. **代码生成错误**：可能由于语法错误或逻辑错误导致生成的代码无法编译
2. **依赖解析错误**：可能由于依赖关系处理不当导致模块加载顺序错误
3. **性能问题**：可能由于代码生成或模块初始化过程过于复杂导致性能问题
4. **兼容性问题**：可能与不同版本的 .NET 或 IDE 存在兼容性问题

## 缓解措施

1. **代码生成错误**：增加代码生成的单元测试，确保生成的代码能够正确编译
2. **依赖解析错误**：增加依赖解析的单元测试，确保依赖关系处理正确
3. **性能问题**：优化代码生成和模块初始化逻辑，减少不必要的计算
4. **兼容性问题**：测试不同版本的 .NET 和 IDE，确保兼容性

## 测试进度

| 任务 | 状态 | 开始日期 | 完成日期 |
|------|------|----------|----------|
| 任务 1: 核心功能测试 | 待开始 | - | - |
| 任务 2: 依赖管理测试 | 待开始 | - | - |
| 任务 3: 代码生成测试 | 待开始 | - | - |
| 任务 4: 生命周期钩子测试 | 待开始 | - | - |
| 任务 5: 与 ABP Framework 类似功能测试 | 待开始 | - | - |
| 任务 6: 性能测试 | 待开始 | - | - |
