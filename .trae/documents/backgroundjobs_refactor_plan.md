# BackgroundJobs 重构计划（抽象服务与具体实现分离）

## [ ] 任务 1: 清理 CrestCreates.BackgroundJobs 中的具体实现
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 移除 `BackgroundJobService` 中对 Quartz 的直接依赖
  - 移除 `BackgroundJobWrapper<T>` 类
  - 保留 `IBackgroundJobService` 和 `IBackgroundJob` 接口
  - 移除 `BackgroundJobsModule` 中的 Quartz 注册逻辑
- **Success Criteria**:
  - `CrestCreates.BackgroundJobs` 不再依赖 Quartz 包
  - 只保留抽象接口和必要的基础类
- **Test Requirements**:
  - `programmatic` TR-1.1: 项目能够成功编译
  - `human-judgement` TR-1.2: 代码结构清晰，只包含抽象定义
- **Notes**: 确保所有抽象接口保持不变，以便向后兼容

## [ ] 任务 2: 创建 CrestCreates.BackgroundJobs.Quartz 项目
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**:
  - 创建新的项目 `CrestCreates.BackgroundJobs.Quartz`
  - 引用 `CrestCreates.BackgroundJobs` 和 Quartz 相关包
  - 实现 `BackgroundJobService` 和 `BackgroundJobWrapper<T>`
  - 创建 `QuartzBackgroundJobsModule` 用于注册服务
- **Success Criteria**:
  - 项目能够成功创建和编译
  - 包含完整的 Quartz 实现
- **Test Requirements**:
  - `programmatic` TR-2.1: 项目能够成功编译
  - `human-judgement` TR-2.2: 代码结构与 EventBus 实现模式一致
- **Notes**: 参考 EventBus 的实现模式，确保模块注册逻辑正确

## [ ] 任务 3: 更新 BackgroundJobsModule 为抽象模块
- **Priority**: P1
- **Depends On**: 任务 1
- **Description**:
  - 修改 `BackgroundJobsModule` 为抽象模块
  - 移除具体的 Quartz 注册逻辑
  - 只保留必要的基础配置
- **Success Criteria**:
  - `BackgroundJobsModule` 不再依赖 Quartz
  - 能够作为基础模块被其他模块引用
- **Test Requirements**:
  - `programmatic` TR-3.1: 项目能够成功编译
  - `human-judgement` TR-3.2: 模块结构清晰，符合抽象设计
- **Notes**: 确保模块能够与具体实现模块正确配合

## [ ] 任务 4: 测试重构后的实现
- **Priority**: P1
- **Depends On**: 任务 2, 任务 3
- **Description**:
  - 验证重构后的代码能够正常工作
  - 确保通过模块注入能够正确注册和使用 Quartz 服务
  - 测试基本的任务调度功能
- **Success Criteria**:
  - 重构后的代码能够正常编译和运行
  - 任务调度功能正常工作
- **Test Requirements**:
  - `programmatic` TR-4.1: 项目能够成功编译
  - `programmatic` TR-4.2: 基本任务调度功能测试通过
- **Notes**: 确保所有原有功能都能正常工作

## [ ] 任务 5: 清理和优化
- **Priority**: P2
- **Depends On**: 任务 4
- **Description**:
  - 清理不必要的代码和文件
  - 优化代码结构和命名
  - 确保代码风格一致
- **Success Criteria**:
  - 代码干净整洁
  - 符合项目的代码风格要求
- **Test Requirements**:
  - `programmatic` TR-5.1: 项目能够成功编译
  - `human-judgement` TR-5.2: 代码风格一致，结构清晰
- **Notes**: 确保所有文件都符合项目的代码规范