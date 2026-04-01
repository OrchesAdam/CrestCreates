# Scheduling 重构计划（抽象服务 + 具体实现分离）

## 背景

用户希望重构 Scheduling 模块，使其只保留服务抽象接口（ISchedulerService），具体实现（如 Quartz、Hangfire）由单独的模块实现，并使用 SourceGenerator 实现服务注册。

## 目标

1. **CrestCreates.Scheduling**：只保留服务抽象接口
2. **CrestCreates.Scheduling.Quartz**：提供 Quartz 具体实现
3. **使用 SourceGenerator**：自动生成服务注册代码

## 任务列表

### 任务 1: 清理 CrestCreates.Scheduling，只保留抽象接口
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 删除 `SchedulerService.cs`（具体实现）
  - 保留 `ISchedulerService.cs`（接口）
  - 保留 `IJob.cs`（任务接口）
  - 保留 `SchedulingModule.cs`（空壳模块，供 ModuleSourceGenerator 使用）
  - 移除 Quartz 包引用
- **Success Criteria**:
  - Scheduling 项目只包含抽象定义
  - 无具体实现代码
  - 编译通过
- **Test Requirements**:
  - `programmatic` TR-1.1: 项目能成功编译
  - `human-judgement` TR-1.2: 代码结构清晰，只有抽象定义

### 任务 2: 创建 CrestCreates.Scheduling.Quartz 项目
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**:
  - 创建新项目 `CrestCreates.Scheduling.Quartz`
  - 引用 `CrestCreates.Scheduling` 和 `Quartz` 包
  - 实现 `SchedulerService` 和 `QuartzBackgroundJobWrapper`
  - 创建 `SchedulingQuartzModule`
  - 使用 SourceGenerator 生成服务注册代码
- **Success Criteria**:
  - 项目结构正确
  - 实现 Scheduling 的具体功能
  - 编译通过
- **Test Requirements**:
  - `programmatic` TR-2.1: 项目能成功编译
  - `human-judgement` TR-2.2: 实现模式与 EventBus 一致

### 任务 3: 创建 SchedulingSourceGenerator
- **Priority**: P0
- **Depends On**: 任务 2
- **Description**:
  - 在 CodeGenerator 中创建 SchedulingSourceGenerator
  - 扫描标记了 `[BackgroundJob]` 特性的类
  - 生成服务注册代码
- **Success Criteria**:
  - SourceGenerator 能正确扫描和生成代码
  - 生成的代码能正确注册服务
- **Test Requirements**:
  - `programmatic` TR-3.1: 编译成功，无错误
  - `human-judgement` TR-3.2: 生成的代码结构正确

### 任务 4: 测试验证
- **Priority**: P0
- **Depends On**: 任务 3
- **Description**:
  - 编译所有相关项目
  - 验证 Scheduling 项目只包含抽象
  - 验证 Scheduling.Quartz 项目包含具体实现
- **Success Criteria**:
  - 所有项目能成功编译
  - 功能正常
- **Test Requirements**:
  - `programmatic` TR-4.1: 所有项目编译成功
  - `human-judgement` TR-4.2: 重构后的代码结构清晰

## 重构后的结构

```
CrestCreates.Scheduling/           (抽象层)
├── Jobs/
│   └── IJob.cs                   # 任务接口
├── Modules/
│   └── SchedulingModule.cs       # 空壳模块
├── Services/
│   └── ISchedulerService.cs      # 调度服务接口
└── CrestCreates.Scheduling.csproj

CrestCreates.Scheduling.Quartz/   (具体实现层)
├── Services/
│   └── SchedulerService.cs       # Quartz 具体实现
├── Middlewares/
│   └── QuartzBackgroundJobWrapper.cs
├── Modules/
│   └── SchedulingQuartzModule.cs # 模块注册
└── CrestCreates.Scheduling.Quartz.csproj
```

## 优势

1. **更好的关注点分离**：Scheduling 负责抽象，Quartz/Hangfire 负责实现
2. **可扩展性**：将来可以添加 `CrestCreates.Scheduling.Hangfire`
3. **灵活性**：用户可以根据需求选择使用哪个实现
4. **一致性**：与 EventBus 的设计模式保持一致