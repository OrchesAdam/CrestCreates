# CrestCreates.CodeGenerator.Tests 构建问题修复计划

## 问题分析

通过检查项目配置，发现以下可能导致构建异常的问题：

### 1. 缺少项目引用
- **问题**：测试项目缺少对 `CrestCreates.Domain.Shared` 的引用
- **影响**：`ModuleAttribute` 特性无法找到，导致模块类编译失败
- **修复**：添加对 `CrestCreates.Domain.Shared` 的项目引用

### 2. 缺少测试框架包引用
- **问题**：测试项目缺少 xUnit 和其他测试相关的 NuGet 包引用
- **影响**：无法编译和运行测试
- **修复**：添加测试框架包引用

### 3. 源代码生成器配置问题
- **问题**：源代码生成器可能无法正确生成代码
- **影响**：`AutoModuleRegistration` 类无法生成
- **修复**：检查生成器配置和模块类的正确性

## 修复步骤

### 任务 1：添加缺少的项目引用
**优先级**：P0
**描述**：
- 在 `CrestCreates.CodeGenerator.Tests.csproj` 中添加对 `CrestCreates.Domain.Shared` 的引用
- 这是使用 `[Module]` 特性的必要条件

**验证方法**：
- 编译项目，确认 `ModuleAttribute` 可以被正确解析

### 任务 2：添加测试框架包引用
**优先级**：P0
**描述**：
- 添加 `Microsoft.NET.Test.Sdk` 包引用
- 添加 `xunit` 包引用
- 添加 `xunit.runner.visualstudio` 包引用
- 添加 `Microsoft.Extensions.Logging` 包引用（用于测试中的日志功能）

**验证方法**：
- 编译项目，确认测试框架可以正常工作

### 任务 3：验证源代码生成器工作正常
**优先级**：P1
**描述**：
- 检查 `ModuleSourceGenerator` 是否能正确识别模块类
- 验证生成的 `AutoModuleRegistration.g.cs` 文件
- 确保模块依赖关系正确解析

**验证方法**：
- 编译项目后检查 `Generated` 文件夹中的生成文件
- 运行测试验证模块注册功能

### 任务 4：修复潜在的命名空间问题
**优先级**：P1
**描述**：
- 检查 `CrestCreates.Domain.Shared` 项目是否正确导出 `ModuleAttribute`
- 确保 `CrestCreates.Infrastructure` 项目正确导出 `ModuleBase` 和 `IModule`

**验证方法**：
- 编译相关项目，确认类型可以正确访问

## 修复后的预期结果

1. **编译成功**：项目能够无错误地编译通过
2. **生成器工作**：`AutoModuleRegistration.g.cs` 文件正确生成
3. **测试运行**：测试可以正常运行并验证模块功能
4. **模块注册**：模块能够正确注册到依赖注入容器中

## 测试验证

修复完成后，应验证以下功能：
- [ ] 项目编译无错误
- [ ] 生成的代码包含 `AutoModuleRegistration` 类
- [ ] `RegisteredModules` 属性包含所有测试模块
- [ ] `RegisterModules` 和 `InitializeModules` 方法可以正常调用
- [ ] 模块生命周期方法按正确顺序执行
