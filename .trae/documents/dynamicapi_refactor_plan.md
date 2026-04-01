# DynamicApi 重构计划（Module + Service 混合处理方式）

## [ ] 任务 1: 修复 DynamicApiModule 方法名
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 将 DynamicApiModule 中的 ConfigureServices 方法改为 OnConfigureServices，与 ModuleBase 保持一致
- **Success Criteria**:
  - 方法名正确，与 ModuleBase 保持一致
  - 项目能够成功编译
- **Test Requirements**:
  - `programmatic` TR-1.1: 项目能够成功编译
  - `human-judgement` TR-1.2: 方法名正确，符合规范
- **Notes**: 确保方法签名与 ModuleBase 中的方法一致

## [ ] 任务 2: 增加 DynamicApiOptions 配置选项
- **Priority**: P1
- **Depends On**: 任务 1
- **Description**:
  - 创建 DynamicApiOptions 类，允许用户自定义扫描的程序集和生成的行为
  - 在 DynamicApiModule 中添加配置选项的注册
- **Success Criteria**:
  - DynamicApiOptions 类创建成功
  - 配置选项能够正确注册和使用
- **Test Requirements**:
  - `programmatic` TR-2.1: 项目能够成功编译
  - `human-judgement` TR-2.2: 配置选项设计合理，易于使用
- **Notes**: 确保配置选项的设计符合项目的整体风格

## [ ] 任务 3: 增强 DynamicApiModule，添加自动扫描功能
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**:
  - 在 DynamicApiModule 中添加 OnApplicationInitialization 方法
  - 实现自动扫描程序集并生成 API 的功能
- **Success Criteria**:
  - 自动扫描功能实现成功
  - 能够正确生成 API
- **Test Requirements**:
  - `programmatic` TR-3.1: 项目能够成功编译
  - `human-judgement` TR-3.2: 自动扫描功能工作正常
- **Notes**: 确保扫描逻辑正确，能够找到所有标记了 DynamicApiAttribute 的接口

## [ ] 任务 4: 优化 DynamicApiGenerator，支持更复杂的 API 生成逻辑
- **Priority**: P1
- **Depends On**: 任务 3
- **Description**:
  - 增强 DynamicApiGenerator 的功能，支持不同的 HTTP 方法
  - 支持路由模板、参数绑定、授权和认证等功能
- **Success Criteria**:
  - DynamicApiGenerator 功能增强成功
  - 能够生成更复杂的 API
- **Test Requirements**:
  - `programmatic` TR-4.1: 项目能够成功编译
  - `human-judgement` TR-4.2: API 生成逻辑正确，功能完善
- **Notes**: 确保 API 生成逻辑符合 ASP.NET Core 的规范

## [ ] 任务 5: 测试重构后的实现
- **Priority**: P0
- **Depends On**: 任务 4
- **Description**:
  - 编译项目，确保所有代码都能正常编译
  - 验证重构后的 DynamicApi 能够正常工作
- **Success Criteria**:
  - 项目能够成功编译
  - DynamicApi 功能正常
- **Test Requirements**:
  - `programmatic` TR-5.1: 项目能够成功编译
  - `human-judgement` TR-5.2: 重构后的代码结构清晰，功能正常
- **Notes**: 确保所有重构后的代码都能正常工作