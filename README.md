# CrestCreates 框架文档

## 框架概述

CrestCreates 是一个功能强大、模块化的 .NET 企业级应用开发框架，提供了一系列核心功能和工具，帮助开发者快速构建高质量的应用程序。

## 核心特性

- **模块化架构**：基于 ModuleBase 模式，支持模块的自动发现和注册
- **代码生成**：使用 SourceGenerator 技术，自动生成重复代码
- **健康检查**：提供完整的健康检查体系
- **任务调度**：基于 Quartz 的任务调度系统
- **事件总线**：支持本地和分布式事件
- **多租户**：内置多租户支持
- **缓存**：多级缓存系统
- **审计日志**：完整的审计日志功能
- **ORM 支持**：支持 EFCore、FreeSql、SqlSugar

## 项目结构

```
CrestCreates/
├── framework/
│   ├── src/              # 框架核心源码
│   │   ├── CrestCreates.Modularity/          # 模块化核心
│   │   ├── CrestCreates.Domain/             # 领域模型
│   │   ├── CrestCreates.Infrastructure/     # 基础设施
│   │   ├── CrestCreates.HealthCheck/        # 健康检查核心
│   │   ├── CrestCreates.HealthCheck.AspNetCore/ # ASP.NET Core 集成
│   │   ├── CrestCreates.HealthCheck.Mvc/    # MVC 控制器
│   │   ├── CrestCreates.Scheduling/         # 任务调度核心
│   │   ├── CrestCreates.Scheduling.Quartz/  # Quartz 实现
│   │   └── ... 其他模块
│   ├── tools/             # 工具和代码生成器
│   │   └── CrestCreates.CodeGenerator/     # 代码生成器
│   └── test/              # 测试项目
├── samples/               # 示例项目
│   └── CrestCreates.Sample.Web/            # Web API 示例
└── Directory.Packages.props  # 中央包管理
```

## 快速开始

### 1. 创建项目

使用 .NET CLI 创建一个新的 ASP.NET Core Web API 项目：

```bash
dotnet new webapi -n YourProjectName
```

### 2. 添加框架引用

编辑项目文件，添加对 CrestCreates 框架模块的引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\framework\src\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
  <ProjectReference Include="..\..\framework\src\CrestCreates.HealthCheck\CrestCreates.HealthCheck.csproj" />
  <ProjectReference Include="..\..\framework\src\CrestCreates.HealthCheck.AspNetCore\CrestCreates.HealthCheck.AspNetCore.csproj" />
  <ProjectReference Include="..\..\framework\src\CrestCreates.HealthCheck.Mvc\CrestCreates.HealthCheck.Mvc.csproj" />
  <ProjectReference Include="..\..\framework\src\CrestCreates.Scheduling\CrestCreates.Scheduling.csproj" />
  <ProjectReference Include="..\..\framework\src\CrestCreates.Scheduling.Quartz\CrestCreates.Scheduling.Quartz.csproj" />
</ItemGroup>
```

### 3. 配置框架

在 `Program.cs` 中注册和初始化框架模块：

```csharp
using CrestCreates.Modularity;

var builder = WebApplication.CreateBuilder(args);

// 注册 CrestCreates 框架模块
builder.Host.RegisterModules();

// 配置其他服务
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 初始化模块
app.Services.GetRequiredService<IHost>().InitializeModules();

// 配置中间件
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 添加路由
app.MapGet("/weatherforecast", () =>
{
    // 实现代码
});

app.Run();
```

## 核心模块使用指南

### 健康检查模块

#### 1. 基本使用

健康检查模块会自动注册并提供以下端点：
- `/health` - 完整健康状态
- `/api/health` - 详细健康信息（来自 MVC 控制器）
- `/api/health/{tag}` - 按标签筛选健康检查

#### 2. 自定义健康检查

创建自定义健康检查类：

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CrestCreates.HealthCheck.Attributes;

[HealthCheck(Name = "Custom", Tags = new[] { "custom" }, Description = "Custom health check")]
public class CustomHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // 健康检查逻辑
        return Task.FromResult(HealthCheckResult.Healthy("Custom health check passed"));
    }
}
```

### 任务调度模块

#### 1. 创建任务

创建继承自 `IJob` 的任务类：

```csharp
using CrestCreates.Scheduling.Jobs;

public class SampleJob : IJob
{
    private readonly ILogger<SampleJob> _logger;

    public SampleJob(ILogger<SampleJob> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sample job executed at: {time}", DateTime.Now);
        await Task.Delay(1000, cancellationToken);
    }
}
```

#### 2. 调度任务

使用 `ISchedulerService` 调度任务：

```csharp
private readonly ISchedulerService _schedulerService;

// 立即执行任务
await _schedulerService.ExecuteJobAsync<SampleJob>();

// 延迟执行任务
var jobId = await _schedulerService.ScheduleJobAsync<SampleJob>(TimeSpan.FromSeconds(5));

// Cron 表达式调度
var jobId = await _schedulerService.ScheduleJobAsync<SampleJob>("*/5 * * * * ?");

// 注册定时任务
var metadata = new JobMetadata
{
    Name = "SampleJob",
    Group = "Samples",
    CronExpression = "*/10 * * * * ?",
    Description = "Sample job",
    Enabled = true
};
await _schedulerService.RegisterJobAsync<SampleJob>(metadata);
```

## 代码生成器

CrestCreates 框架提供了强大的代码生成器，支持以下功能：

- **模块生成**：自动生成模块注册代码
- **控制器生成**：基于服务接口生成控制器
- **服务生成**：生成服务实现和异常类
- **实体生成**：生成实体相关代码
- **事件总线生成**：生成事件处理器
- **健康检查生成**：生成健康检查注册代码

## 示例项目

框架包含一个完整的示例项目 `CrestCreates.Sample.Web`，演示了：

- 框架模块的注册和使用
- 健康检查的集成
- 任务调度的使用
- 示例任务和控制器

## 配置和依赖

### 中央包管理

框架使用中央包管理，所有包版本定义在 `Directory.Packages.props` 文件中。

### 目标框架

- 框架核心：.NET 10.0
- 代码生成器：.NET Standard 2.0

## 贡献指南

1. 克隆仓库
2. 安装依赖：`dotnet restore`
3. 构建项目：`dotnet build`
4. 运行测试：`dotnet test`

## 许可证

MIT License