# Distributed Transaction CAP 短期改进设计

**Date:** 2026-04-26
**Status:** Approved
**Parent:** CrestCreates.DistributedTransaction.CAP

## 1. Overview

本设计解决分布式事务模块的两个短期改进：
1. **事务日志持久化** — 从内存实现改为数据库存储
2. **补偿器完整实现** — 支持持久化、重试、Source Generator 自动注册

### Scope Summary

| 改进项 | 描述 | Impact Files |
|--------|------|--------------|
| 事务日志持久化 | 数据库存储事务状态 | `PersistentTransactionLogger.cs` |
| 补偿器持久化 | 支持跨重启恢复 | `PersistentTransactionCompensator.cs` |
| Source Generator | 自动生成补偿执行器注册 | `CompensationExecutorGenerator.cs` |
| 后台重试服务 | 指数退避重试 | `CompensationRetryBackgroundService.cs` |

## 2. Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
│  ┌──────────────────────────────────────────────────────┐   │
│  │           IDistributedTransactionManager              │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Domain Layer                              │
│  ┌────────────────────┐    ┌────────────────────────────┐   │
│  │ ITransactionLogger │    │ ITransactionCompensator    │   │
│  └────────────────────┘    └────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                 Infrastructure Layer                         │
│  ┌────────────────────┐    ┌────────────────────────────┐   │
│  │ PersistentTrans-    │    │ PersistentTrans-           │   │
│  │ actionLogger        │    │ actionCompensator          │   │
│  └────────────────────┘    └────────────────────────────┘   │
│                              │                               │
│                              ▼                               │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  IRepository<TransactionLog>                         │   │
│  │  IRepository<TransactionCompensation>                │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

**依赖方向**: Infrastructure → Domain → Abstractions

## 3. Entity Design

### 3.1 TransactionLog

```csharp
// CrestCreates.DistributedTransaction/Models/TransactionLog.cs
public class TransactionLog : IEntity<Guid>
{
    public Guid Id { get; set; }                    // 事务 ID
    public TransactionStatus Status { get; set; }   // 事务状态
    public string? Message { get; set; }            // 日志消息
    public string? ErrorDetails { get; set; }       // 错误详情 (JSON)
    public int ParticipantCount { get; set; }       // 参与者数量
    public Guid? CorrelationId { get; set; }        // 关联 ID (分布式追踪)
    public string? Metadata { get; set; }           // 扩展元数据 (JSON)
    public DateTime CreatedAt { get; set; }         // 创建时间
    public DateTime UpdatedAt { get; set; }         // 最后更新时间
}
```

### 3.2 TransactionCompensation

```csharp
// CrestCreates.DistributedTransaction/Models/TransactionCompensation.cs
public class TransactionCompensation : IEntity<Guid>
{
    public Guid Id { get; set; }                    // 补偿记录 ID
    public Guid TransactionId { get; set; }         // 关联事务 ID
    public string ParticipantName { get; set; }     // 参与者名称
    public string? CompensationData { get; set; }   // 补偿数据 (JSON)
    public CompensationStatus Status { get; set; }  // 补偿状态
    public int RetryCount { get; set; }             // 已重试次数
    public int MaxRetries { get; set; }             // 最大重试次数
    public string? ErrorMessage { get; set; }       // 错误信息
    public DateTime CreatedAt { get; set; }         // 创建时间
    public DateTime? ExecutedAt { get; set; }       // 执行时间
}

public enum CompensationStatus
{
    Pending,        // 待执行
    Executing,      // 执行中
    Completed,      // 已完成
    Failed,         // 失败
    Retrying        // 重试中
}
```

## 4. Interface Design

### 4.1 ITransactionLogger (扩展)

```csharp
public interface ITransactionLogger
{
    // 现有方法
    Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string message = null);
    Task LogTransactionErrorAsync(Guid transactionId, Exception exception);
    Task<TransactionStatus?> GetTransactionStatusAsync(Guid transactionId);

    // 新增方法
    Task<TransactionLog?> GetTransactionAsync(Guid transactionId);
    Task LogParticipantCountAsync(Guid transactionId, int count);
}
```

### 4.2 ITransactionCompensator (扩展)

```csharp
public interface ITransactionCompensator
{
    // 现有方法
    Task CompensateAsync(Guid transactionId);
    Task<bool> CanCompensateAsync(Guid transactionId);

    // 新增方法
    Task RegisterCompensationAsync(
        Guid transactionId,
        string participantName,
        object? compensationData);

    Task<IEnumerable<TransactionCompensation>> GetPendingCompensationsAsync(Guid transactionId);
    Task MarkCompensationCompletedAsync(Guid compensationId);
    Task MarkCompensationFailedAsync(Guid compensationId, string errorMessage);
    Task ProcessRetryingCompensationsAsync();
}
```

### 4.3 ICompensationExecutor (新增)

```csharp
// 补偿执行器接口 - 由应用层实现具体补偿逻辑
public interface ICompensationExecutor
{
    string Name { get; }
    Task ExecuteAsync(string? compensationData);
}
```

### 4.4 ICompensationExecutorRegistry (新增)

```csharp
// 补偿执行器注册表 - 由 Source Generator 生成实现
public interface ICompensationExecutorRegistry
{
    ICompensationExecutor? GetExecutor(string name);
    IEnumerable<ICompensationExecutor> GetAll();
}
```

## 5. Implementation

### 5.1 PersistentTransactionLogger

```csharp
// CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionLogger.cs
public class PersistentTransactionLogger : ITransactionLogger
{
    private readonly IRepository<TransactionLog, Guid> _repository;

    public PersistentTransactionLogger(IRepository<TransactionLog, Guid> repository)
    {
        _repository = repository;
    }

    public async Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string message = null)
    {
        var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);

        if (log == null)
        {
            log = new TransactionLog
            {
                Id = transactionId,
                CreatedAt = DateTime.UtcNow
            };
            await _repository.AddAsync(log);
        }

        log.Status = status;
        log.Message = message;
        log.UpdatedAt = DateTime.UtcNow;

        if (log != null)
            await _repository.UpdateAsync(log);
    }

    public async Task LogTransactionErrorAsync(Guid transactionId, Exception exception)
    {
        var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
        if (log != null)
        {
            log.ErrorDetails = System.Text.Json.JsonSerializer.Serialize(new
            {
                exception.Message,
                exception.StackTrace,
                Type = exception.GetType().FullName
            });
            log.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(log);
        }
    }

    public async Task<TransactionStatus?> GetTransactionStatusAsync(Guid transactionId)
    {
        var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
        return log?.Status;
    }

    public async Task<TransactionLog?> GetTransactionAsync(Guid transactionId)
    {
        return await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
    }

    public async Task LogParticipantCountAsync(Guid transactionId, int count)
    {
        var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
        if (log != null)
        {
            log.ParticipantCount = count;
            log.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(log);
        }
    }
}
```

### 5.2 PersistentTransactionCompensator

```csharp
// CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionCompensator.cs
public class PersistentTransactionCompensator : ITransactionCompensator
{
    private readonly IRepository<TransactionCompensation, Guid> _compensationRepo;
    private readonly IRepository<TransactionLog, Guid> _transactionRepo;
    private readonly ICompensationExecutorRegistry _executorRegistry;
    private readonly DistributedTransactionCapOptions _options;

    public PersistentTransactionCompensator(
        IRepository<TransactionCompensation, Guid> compensationRepo,
        IRepository<TransactionLog, Guid> transactionRepo,
        ICompensationExecutorRegistry executorRegistry,
        IOptions<DistributedTransactionCapOptions> options)
    {
        _compensationRepo = compensationRepo;
        _transactionRepo = transactionRepo;
        _executorRegistry = executorRegistry;
        _options = options.Value;
    }

    public async Task RegisterCompensationAsync(
        Guid transactionId,
        string participantName,
        object? compensationData)
    {
        var compensation = new TransactionCompensation
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            ParticipantName = participantName,
            CompensationData = compensationData != null
                ? System.Text.Json.JsonSerializer.Serialize(compensationData)
                : null,
            Status = CompensationStatus.Pending,
            MaxRetries = _options.CompensationMaxRetries,
            CreatedAt = DateTime.UtcNow
        };

        await _compensationRepo.AddAsync(compensation);
    }

    public async Task CompensateAsync(Guid transactionId)
    {
        var compensations = await GetPendingCompensationsAsync(transactionId);

        // 按创建时间逆序执行（后进先出）
        foreach (var compensation in compensations.OrderByDescending(x => x.CreatedAt))
        {
            await ExecuteCompensationAsync(compensation);
        }
    }

    public async Task<IEnumerable<TransactionCompensation>> GetPendingCompensationsAsync(Guid transactionId)
    {
        return await _compensationRepo.FindAsync(
            x => x.TransactionId == transactionId &&
                 (x.Status == CompensationStatus.Pending || x.Status == CompensationStatus.Retrying));
    }

    public async Task MarkCompensationCompletedAsync(Guid compensationId)
    {
        var compensation = await _compensationRepo.GetByIdAsync(compensationId);
        if (compensation != null)
        {
            compensation.Status = CompensationStatus.Completed;
            compensation.ExecutedAt = DateTime.UtcNow;
            await _compensationRepo.UpdateAsync(compensation);
        }
    }

    public async Task MarkCompensationFailedAsync(Guid compensationId, string errorMessage)
    {
        var compensation = await _compensationRepo.GetByIdAsync(compensationId);
        if (compensation != null)
        {
            compensation.Status = CompensationStatus.Failed;
            compensation.ErrorMessage = errorMessage;
            await _compensationRepo.UpdateAsync(compensation);
        }
    }

    public async Task ProcessRetryingCompensationsAsync()
    {
        var retryingCompensations = await _compensationRepo.FindAsync(
            x => x.Status == CompensationStatus.Retrying);

        foreach (var compensation in retryingCompensations)
        {
            var delay = CalculateRetryDelay(compensation.RetryCount);
            if (DateTime.UtcNow < compensation.UpdatedAt.Add(delay))
                continue;

            await ExecuteCompensationAsync(compensation);
        }
    }

    private async Task ExecuteCompensationAsync(TransactionCompensation compensation)
    {
        var executor = _executorRegistry.GetExecutor(compensation.ParticipantName);
        if (executor == null)
        {
            throw new InvalidOperationException(
                $"No executor found for participant: {compensation.ParticipantName}");
        }

        compensation.Status = CompensationStatus.Executing;
        await _compensationRepo.UpdateAsync(compensation);

        try
        {
            await executor.ExecuteAsync(compensation.CompensationData);
            await MarkCompensationCompletedAsync(compensation.Id);
        }
        catch (Exception ex)
        {
            await HandleCompensationFailureAsync(compensation, ex);
        }
    }

    private async Task HandleCompensationFailureAsync(
        TransactionCompensation compensation,
        Exception ex)
    {
        compensation.RetryCount++;
        compensation.ErrorMessage = ex.Message;
        compensation.UpdatedAt = DateTime.UtcNow;

        if (compensation.RetryCount >= compensation.MaxRetries)
        {
            compensation.Status = CompensationStatus.Failed;
        }
        else
        {
            compensation.Status = CompensationStatus.Retrying;
        }

        await _compensationRepo.UpdateAsync(compensation);
    }

    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        var baseInterval = _options.CompensationRetryIntervalSeconds;
        var delay = baseInterval * Math.Pow(2, retryCount - 1);
        return TimeSpan.FromSeconds(Math.Min(delay, 300)); // 最大 5 分钟
    }

    public async Task<bool> CanCompensateAsync(Guid transactionId)
    {
        var compensations = await _compensationRepo.FindAsync(
            x => x.TransactionId == transactionId &&
                 x.Status != CompensationStatus.Failed);

        return compensations.Any();
    }
}
```

## 6. Source Generator

### 6.1 CompensationExecutorAttribute

```csharp
// CrestCreates.DistributedTransaction/Attributes/CompensationExecutorAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CompensationExecutorAttribute : Attribute
{
}
```

### 6.2 用户使用方式

```csharp
[CompensationExecutor]
public class OrderCompensationExecutor : ICompensationExecutor
{
    public string Name => "Order";
    public Task ExecuteAsync(string? compensationData)
    {
        // 补偿逻辑
    }
}

[CompensationExecutor]
public class InventoryCompensationExecutor : ICompensationExecutor
{
    public string Name => "Inventory";
    public Task ExecuteAsync(string? compensationData)
    {
        // 补偿逻辑
    }
}
```

### 6.3 Generator 输出

```csharp
// CompensationExecutorRegistry.g.cs (自动生成)
public class CompensationExecutorRegistry : ICompensationExecutorRegistry
{
    private readonly OrderCompensationExecutor _orderCompensationExecutor;
    private readonly InventoryCompensationExecutor _inventoryCompensationExecutor;

    public CompensationExecutorRegistry(
        OrderCompensationExecutor orderCompensationExecutor,
        InventoryCompensationExecutor inventoryCompensationExecutor)
    {
        _orderCompensationExecutor = orderCompensationExecutor;
        _inventoryCompensationExecutor = inventoryCompensationExecutor;
    }

    public ICompensationExecutor? GetExecutor(string name) => name switch
    {
        "Order" => _orderCompensationExecutor,
        "Inventory" => _inventoryCompensationExecutor,
        _ => null
    };

    public IEnumerable<ICompensationExecutor> GetAll() => new ICompensationExecutor[]
    {
        _orderCompensationExecutor,
        _inventoryCompensationExecutor
    };
}

// CompensationExecutorServiceCollectionExtensions.g.cs (自动生成)
public static class CompensationExecutorServiceCollectionExtensions
{
    public static IServiceCollection AddCompensationExecutors(this IServiceCollection services)
    {
        services.AddScoped<OrderCompensationExecutor>();
        services.AddScoped<InventoryCompensationExecutor>();
        services.AddScoped<ICompensationExecutorRegistry, CompensationExecutorRegistry>();
        return services;
    }
}
```

## 7. Background Service

### 7.1 CompensationRetryBackgroundService

```csharp
// CrestCreates.DistributedTransaction.CAP/BackgroundServices/CompensationRetryBackgroundService.cs
public class CompensationRetryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DistributedTransactionCapOptions _options;

    public CompensationRetryBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<DistributedTransactionCapOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRetryingCompensationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Log error but continue
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.CompensationRetryIntervalSeconds),
                stoppingToken);
        }
    }

    private async Task ProcessRetryingCompensationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var compensator = scope.ServiceProvider
            .GetRequiredService<ITransactionCompensator>();

        await compensator.ProcessRetryingCompensationsAsync();
    }
}
```

## 8. Configuration

### 8.1 Options Extension

```csharp
// CrestCreates.DistributedTransaction.CAP/Options/DistributedTransactionCapOptions.cs
public class DistributedTransactionCapOptions
{
    public const string SectionName = "DistributedTransaction:CAP";

    // 现有配置
    public CapStorageProvider StorageProvider { get; set; } = CapStorageProvider.SqlServer;
    public string StorageConnectionString { get; set; } = string.Empty;
    public CapTransportProvider TransportProvider { get; set; } = CapTransportProvider.RabbitMQ;
    public string TransportConnectionString { get; set; } = "localhost";
    public string DefaultGroup { get; set; } = "crestcreates";
    public int FailedRetryCount { get; set; } = 5;
    public int FailedRetryIntervalSeconds { get; set; } = 60;
    public bool UseDashboard { get; set; }

    // 新增配置
    public int CompensationMaxRetries { get; set; } = 3;
    public int CompensationRetryIntervalSeconds { get; set; } = 30;
    public bool EnableCompensationBackgroundWorker { get; set; } = true;
}
```

### 8.2 Service Registration

```csharp
// CrestCreates.DistributedTransaction.CAP/Extensions/DistributedTransactionCapServiceCollectionExtensions.cs
public static IServiceCollection AddCrestCapDistributedTransaction(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<DistributedTransactionCapOptions>? configure = null)
{
    // ... 现有注册 ...

    // 替换为持久化实现
    services.AddScoped<ITransactionLogger, PersistentTransactionLogger>();
    services.AddScoped<ITransactionCompensator, PersistentTransactionCompensator>();

    // 后台重试任务（可选）
    if (options.EnableCompensationBackgroundWorker)
    {
        services.AddHostedService<CompensationRetryBackgroundService>();
    }

    return services;
}
```

### 8.3 User Usage

```csharp
// Program.cs
builder.Services.AddCrestCapDistributedTransaction(
    builder.Configuration,
    options =>
    {
        options.CompensationMaxRetries = 5;
        options.CompensationRetryIntervalSeconds = 60;
    });

// 注册补偿执行器 (Source Generator 生成)
builder.Services.AddCompensationExecutors();
```

## 9. Database Schema

### 9.1 SQL Server

```sql
-- 事务日志表
CREATE TABLE [dbo].[TransactionLog] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Status] INT NOT NULL,
    [Message] NVARCHAR(500) NULL,
    [ErrorDetails] NVARCHAR(MAX) NULL,
    [ParticipantCount] INT NOT NULL DEFAULT 0,
    [CorrelationId] UNIQUEIDENTIFIER NULL,
    [Metadata] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL,
    [UpdatedAt] DATETIME2 NOT NULL
);

CREATE INDEX IX_TransactionLog_Status ON [dbo].[TransactionLog] ([Status]);
CREATE INDEX IX_TransactionLog_CorrelationId ON [dbo].[TransactionLog] ([CorrelationId]);
CREATE INDEX IX_TransactionLog_CreatedAt ON [dbo].[TransactionLog] ([CreatedAt]);

-- 补偿记录表
CREATE TABLE [dbo].[TransactionCompensation] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [TransactionId] UNIQUEIDENTIFIER NOT NULL,
    [ParticipantName] NVARCHAR(200) NOT NULL,
    [CompensationData] NVARCHAR(MAX) NULL,
    [Status] INT NOT NULL,
    [RetryCount] INT NOT NULL DEFAULT 0,
    [MaxRetries] INT NOT NULL DEFAULT 3,
    [ErrorMessage] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL,
    [ExecutedAt] DATETIME2 NULL,
    [UpdatedAt] DATETIME2 NOT NULL,
    CONSTRAINT FK_TransactionCompensation_TransactionLog
        FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[TransactionLog] ([Id])
);

CREATE INDEX IX_TransactionCompensation_TransationId
    ON [dbo].[TransactionCompensation] ([TransactionId]);
CREATE INDEX IX_TransactionCompensation_Status
    ON [dbo].[TransactionCompensation] ([Status]);
```

## 10. AOT Compatibility

| 组件 | 状态 | 说明 |
|------|------|------|
| 实体类 | ✅ | 纯 POCO，无反射 |
| 仓储操作 | ✅ | 通过 IRepository 接口 |
| 补偿执行器注册 | ✅ | Source Generator 生成静态代码 |
| 后台服务 | ✅ | 标准 IHostedService |
| JSON 序列化 | ✅ | System.Text.Json 源生成器可用 |

## 11. File Summary

### New Files

| File | Location |
|------|----------|
| `TransactionLog.cs` | `CrestCreates.DistributedTransaction/Models/` |
| `TransactionCompensation.cs` | `CrestCreates.DistributedTransaction/Models/` |
| `CompensationStatus.cs` | `CrestCreates.DistributedTransaction/Models/` |
| `ICompensationExecutor.cs` | `CrestCreates.DistributedTransaction/Abstractions/` |
| `ICompensationExecutorRegistry.cs` | `CrestCreates.DistributedTransaction/Abstractions/` |
| `CompensationExecutorAttribute.cs` | `CrestCreates.DistributedTransaction/Attributes/` |
| `PersistentTransactionLogger.cs` | `CrestCreates.DistributedTransaction.CAP/Implementations/` |
| `PersistentTransactionCompensator.cs` | `CrestCreates.DistributedTransaction.CAP/Implementations/` |
| `CompensationRetryBackgroundService.cs` | `CrestCreates.DistributedTransaction.CAP/BackgroundServices/` |
| `CompensationExecutorGenerator.cs` | `CrestCreates.CodeGenerator/CompensationExecutorGenerator/` |

### Modified Files

| File | Changes |
|------|---------|
| `ITransactionLogger.cs` | 新增方法 |
| `ITransactionCompensator.cs` | 新增方法 |
| `DistributedTransactionCapOptions.cs` | 新增配置项 |
| `DistributedTransactionCapServiceCollectionExtensions.cs` | 更新注册逻辑 |

## 12. Acceptance Criteria

- [ ] TransactionLog 实体创建并实现 IEntity<Guid>
- [ ] TransactionCompensation 实体创建并实现 IEntity<Guid>
- [ ] PersistentTransactionLogger 实现所有 ITransactionLogger 方法
- [ ] PersistentTransactionCompensator 实现所有 ITransactionCompensator 方法
- [ ] CompensationExecutorGenerator 正确生成 Registry 和扩展方法
- [ ] CompensationRetryBackgroundService 正确处理重试逻辑
- [ ] 所有新代码 AOT 友好（无反射、无 Activator）
- [ ] 单元测试覆盖核心逻辑
- [ ] 集成测试验证数据库持久化
