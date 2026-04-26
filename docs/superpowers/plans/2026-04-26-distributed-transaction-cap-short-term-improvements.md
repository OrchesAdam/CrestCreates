# Distributed Transaction CAP Short-Term Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement persistent transaction logging and compensation with AOT-friendly Source Generator for executor registration.

**Architecture:** Two new entities (TransactionLog, TransactionCompensation) stored via IRepository interface. Persistent implementations replace in-memory versions. Source Generator scans [CompensationExecutor] attributes and generates static registration code.

**Tech Stack:** Roslyn IIncrementalGenerator, IRepository<TEntity, TKey>, IHostedService, System.Text.Json

---

## File Structure

### New Files

| File | Responsibility |
|------|----------------|
| `TransactionLog.cs` | Transaction log entity |
| `TransactionCompensation.cs` | Compensation record entity |
| `CompensationStatus.cs` | Compensation status enum |
| `ICompensationExecutor.cs` | Compensation executor interface |
| `ICompensationExecutorRegistry.cs` | Executor registry interface |
| `CompensationExecutorAttribute.cs` | Marker attribute for Source Generator |
| `PersistentTransactionLogger.cs` | Database-backed transaction logger |
| `PersistentTransactionCompensator.cs` | Database-backed compensator |
| `CompensationRetryBackgroundService.cs` | Background retry processor |
| `CompensationExecutorGenerator.cs` | Source Generator |
| `CompensationExecutorModel.cs` | Generator model |
| `CompensationExecutorCodeWriter.cs` | Generator code writer |

### Modified Files

| File | Changes |
|------|---------|
| `ITransactionLogger.cs` | Add GetTransactionAsync, LogParticipantCountAsync |
| `ITransactionCompensator.cs` | Add RegisterCompensationAsync, GetPendingCompensationsAsync, etc. |
| `DistributedTransactionCapOptions.cs` | Add compensation retry config |
| `DistributedTransactionCapServiceCollectionExtensions.cs` | Register persistent implementations |

---

## Task 1: Entity Models

**Files:**
- Create: `framework/src/CrestCreates.DistributedTransaction/Models/TransactionLog.cs`
- Create: `framework/src/CrestCreates.DistributedTransaction/Models/TransactionCompensation.cs`
- Create: `framework/src/CrestCreates.DistributedTransaction/Models/CompensationStatus.cs`

- [ ] **Step 1: Write TransactionLog entity**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Models/TransactionLog.cs
using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.DistributedTransaction.Models
{
    public class TransactionLog : IEntity<Guid>
    {
        public Guid Id { get; set; }
        public TransactionStatus Status { get; set; }
        public string? Message { get; set; }
        public string? ErrorDetails { get; set; }
        public int ParticipantCount { get; set; }
        public Guid? CorrelationId { get; set; }
        public string? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
```

- [ ] **Step 2: Write TransactionCompensation entity**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Models/TransactionCompensation.cs
using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.DistributedTransaction.Models
{
    public class TransactionCompensation : IEntity<Guid>
    {
        public Guid Id { get; set; }
        public Guid TransactionId { get; set; }
        public string ParticipantName { get; set; } = string.Empty;
        public string? CompensationData { get; set; }
        public CompensationStatus Status { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
    }
}
```

- [ ] **Step 3: Write CompensationStatus enum**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Models/CompensationStatus.cs
namespace CrestCreates.DistributedTransaction.Models
{
    public enum CompensationStatus
    {
        Pending = 0,
        Executing = 1,
        Completed = 2,
        Failed = 3,
        Retrying = 4
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction/CrestCreates.DistributedTransaction.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction/Models/TransactionLog.cs \
        framework/src/CrestCreates.DistributedTransaction/Models/TransactionCompensation.cs \
        framework/src/CrestCreates.DistributedTransaction/Models/CompensationStatus.cs
git commit -m "feat(DistributedTransaction): add TransactionLog and TransactionCompensation entities"
```

---

## Task 2: Interface Extensions

**Files:**
- Modify: `framework/src/CrestCreates.DistributedTransaction/Abstractions/ITransactionLogger.cs`
- Modify: `framework/src/CrestCreates.DistributedTransaction/Abstractions/ITransactionCompensator.cs`
- Create: `framework/src/CrestCreates.DistributedTransaction/Abstractions/ICompensationExecutor.cs`
- Create: `framework/src/CrestCreates.DistributedTransaction/Abstractions/ICompensationExecutorRegistry.cs`

- [ ] **Step 1: Extend ITransactionLogger**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Abstractions/ITransactionLogger.cs
using System;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ITransactionLogger
    {
        Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string? message = null);
        Task LogTransactionErrorAsync(Guid transactionId, Exception exception);
        Task<TransactionStatus?> GetTransactionStatusAsync(Guid transactionId);

        // New methods
        Task<TransactionLog?> GetTransactionAsync(Guid transactionId);
        Task LogParticipantCountAsync(Guid transactionId, int count);
    }
}
```

- [ ] **Step 2: Extend ITransactionCompensator**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Abstractions/ITransactionCompensator.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ITransactionCompensator
    {
        Task CompensateAsync(Guid transactionId);
        Task<bool> CanCompensateAsync(Guid transactionId);

        // New methods
        Task RegisterCompensationAsync(
            Guid transactionId,
            string participantName,
            object? compensationData);

        Task<IEnumerable<TransactionCompensation>> GetPendingCompensationsAsync(Guid transactionId);
        Task MarkCompensationCompletedAsync(Guid compensationId);
        Task MarkCompensationFailedAsync(Guid compensationId, string errorMessage);
        Task ProcessRetryingCompensationsAsync();
    }
}
```

- [ ] **Step 3: Create ICompensationExecutor**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Abstractions/ICompensationExecutor.cs
using System.Threading.Tasks;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ICompensationExecutor
    {
        string Name { get; }
        Task ExecuteAsync(string? compensationData);
    }
}
```

- [ ] **Step 4: Create ICompensationExecutorRegistry**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Abstractions/ICompensationExecutorRegistry.cs
using System.Collections.Generic;

namespace CrestCreates.DistributedTransaction.Abstractions
{
    public interface ICompensationExecutorRegistry
    {
        ICompensationExecutor? GetExecutor(string name);
        IEnumerable<ICompensationExecutor> GetAll();
    }
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction/CrestCreates.DistributedTransaction.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction/Abstractions/
git commit -m "feat(DistributedTransaction): extend interfaces for persistent logging and compensation"
```

---

## Task 3: CompensationExecutor Attribute

**Files:**
- Create: `framework/src/CrestCreates.DistributedTransaction/Attributes/CompensationExecutorAttribute.cs`

- [ ] **Step 1: Create attribute**

```csharp
// framework/src/CrestCreates.DistributedTransaction/Attributes/CompensationExecutorAttribute.cs
using System;

namespace CrestCreates.DistributedTransaction.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class CompensationExecutorAttribute : Attribute
    {
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction/CrestCreates.DistributedTransaction.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction/Attributes/CompensationExecutorAttribute.cs
git commit -m "feat(DistributedTransaction): add CompensationExecutorAttribute for Source Generator"
```

---

## Task 4: PersistentTransactionLogger Implementation

**Files:**
- Create: `framework/src/CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionLogger.cs`

- [ ] **Step 1: Write PersistentTransactionLogger**

```csharp
// framework/src/CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionLogger.cs
using System;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class PersistentTransactionLogger : ITransactionLogger
    {
        private readonly IRepository<TransactionLog, Guid> _repository;

        public PersistentTransactionLogger(IRepository<TransactionLog, Guid> repository)
        {
            _repository = repository;
        }

        public async Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string? message = null)
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
            await _repository.UpdateAsync(log);
        }

        public async Task LogTransactionErrorAsync(Guid transactionId, Exception exception)
        {
            var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
            if (log == null)
            {
                log = new TransactionLog
                {
                    Id = transactionId,
                    CreatedAt = DateTime.UtcNow,
                    Status = TransactionStatus.Failed
                };
                await _repository.AddAsync(log);
            }

            log.ErrorDetails = JsonSerializer.Serialize(new
            {
                exception.Message,
                exception.StackTrace,
                Type = exception.GetType().FullName
            });
            log.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(log);
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
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction.CAP/CrestCreates.DistributedTransaction.CAP.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionLogger.cs
git commit -m "feat(DistributedTransaction.CAP): implement PersistentTransactionLogger"
```

---

## Task 5: PersistentTransactionCompensator Implementation

**Files:**
- Create: `framework/src/CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionCompensator.cs`

- [ ] **Step 1: Write PersistentTransactionCompensator**

```csharp
// framework/src/CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionCompensator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;
using CrestCreates.DistributedTransaction.CAP.Options;
using CrestCreates.OrmProviders.Abstract.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class PersistentTransactionCompensator : ITransactionCompensator
    {
        private readonly IRepository<TransactionCompensation, Guid> _compensationRepo;
        private readonly ICompensationExecutorRegistry _executorRegistry;
        private readonly DistributedTransactionCapOptions _options;

        public PersistentTransactionCompensator(
            IRepository<TransactionCompensation, Guid> compensationRepo,
            ICompensationExecutorRegistry executorRegistry,
            IOptions<DistributedTransactionCapOptions> options)
        {
            _compensationRepo = compensationRepo;
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
                    ? JsonSerializer.Serialize(compensationData)
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

        public async Task<bool> CanCompensateAsync(Guid transactionId)
        {
            var compensations = await _compensationRepo.FindAsync(
                x => x.TransactionId == transactionId &&
                     x.Status != CompensationStatus.Failed);

            return compensations.Any();
        }

        private async Task ExecuteCompensationAsync(TransactionCompensation compensation)
        {
            var executor = _executorRegistry.GetExecutor(compensation.ParticipantName);
            if (executor == null)
            {
                await MarkCompensationFailedAsync(
                    compensation.Id,
                    $"No executor found for participant: {compensation.ParticipantName}");
                return;
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
            return TimeSpan.FromSeconds(Math.Min(delay, 300));
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction.CAP/CrestCreates.DistributedTransaction.CAP.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionCompensator.cs
git commit -m "feat(DistributedTransaction.CAP): implement PersistentTransactionCompensator with retry logic"
```

---

## Task 6: Background Retry Service

**Files:**
- Create: `framework/src/CrestCreates.DistributedTransaction.CAP/BackgroundServices/CompensationRetryBackgroundService.cs`

- [ ] **Step 1: Write CompensationRetryBackgroundService**

```csharp
// framework/src/CrestCreates.DistributedTransaction.CAP/BackgroundServices/CompensationRetryBackgroundService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.DistributedTransaction.CAP.BackgroundServices
{
    public class CompensationRetryBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly DistributedTransactionCapOptions _options;
        private readonly ILogger<CompensationRetryBackgroundService> _logger;

        public CompensationRetryBackgroundService(
            IServiceProvider serviceProvider,
            IOptions<DistributedTransactionCapOptions> options,
            ILogger<CompensationRetryBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Compensation retry background service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRetryingCompensationsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing retrying compensations");
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
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction.CAP/CrestCreates.DistributedTransaction.CAP.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction.CAP/BackgroundServices/CompensationRetryBackgroundService.cs
git commit -m "feat(DistributedTransaction.CAP): add CompensationRetryBackgroundService"
```

---

## Task 7: Configuration Options Extension

**Files:**
- Modify: `framework/src/CrestCreates.DistributedTransaction.CAP/Options/DistributedTransactionCapOptions.cs`

- [ ] **Step 1: Read current options file**

Current content:
```csharp
namespace CrestCreates.DistributedTransaction.CAP.Options;

public class DistributedTransactionCapOptions
{
    public const string SectionName = "DistributedTransaction:CAP";

    public CapStorageProvider StorageProvider { get; set; } = CapStorageProvider.SqlServer;

    public string StorageConnectionString { get; set; } = string.Empty;

    public CapTransportProvider TransportProvider { get; set; } = CapTransportProvider.RabbitMQ;

    public string TransportConnectionString { get; set; } = "localhost";

    public string DefaultGroup { get; set; } = "crestcreates";

    public int FailedRetryCount { get; set; } = 5;

    public int FailedRetryIntervalSeconds { get; set; } = 60;

    public bool UseDashboard { get; set; }
}
```

- [ ] **Step 2: Add compensation retry options**

```csharp
namespace CrestCreates.DistributedTransaction.CAP.Options;

public class DistributedTransactionCapOptions
{
    public const string SectionName = "DistributedTransaction:CAP";

    public CapStorageProvider StorageProvider { get; set; } = CapStorageProvider.SqlServer;

    public string StorageConnectionString { get; set; } = string.Empty;

    public CapTransportProvider TransportProvider { get; set; } = CapTransportProvider.RabbitMQ;

    public string TransportConnectionString { get; set; } = "localhost";

    public string DefaultGroup { get; set; } = "crestcreates";

    public int FailedRetryCount { get; set; } = 5;

    public int FailedRetryIntervalSeconds { get; set; } = 60;

    public bool UseDashboard { get; set; }

    // Compensation retry options
    public int CompensationMaxRetries { get; set; } = 3;

    public int CompensationRetryIntervalSeconds { get; set; } = 30;

    public bool EnableCompensationBackgroundWorker { get; set; } = true;
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction.CAP/CrestCreates.DistributedTransaction.CAP.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction.CAP/Options/DistributedTransactionCapOptions.cs
git commit -m "feat(DistributedTransaction.CAP): add compensation retry configuration options"
```

---

## Task 8: Service Registration Extension

**Files:**
- Modify: `framework/src/CrestCreates.DistributedTransaction.CAP/Extensions/DistributedTransactionCapServiceCollectionExtensions.cs`

- [ ] **Step 1: Read current extension file**

Current content is in the design spec. Key changes needed:
1. Replace `TransactionLogger` with `PersistentTransactionLogger`
2. Replace `DefaultTransactionCompensator` with `PersistentTransactionCompensator`
3. Add background service registration

- [ ] **Step 2: Update service registration**

```csharp
// framework/src/CrestCreates.DistributedTransaction.CAP/Extensions/DistributedTransactionCapServiceCollectionExtensions.cs
using System;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Abstractions;
using CrestCreates.DistributedTransaction.CAP.BackgroundServices;
using CrestCreates.DistributedTransaction.CAP.Implementations;
using CrestCreates.DistributedTransaction.CAP.Options;
using CrestCreates.EventBus.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DistributedTransaction.CAP.Extensions;

public static class DistributedTransactionCapServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCapDistributedTransaction(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DistributedTransactionCapOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new DistributedTransactionCapOptions();
        configuration.GetSection(DistributedTransactionCapOptions.SectionName).Bind(options);
        configure?.Invoke(options);

        services.Configure<DistributedTransactionCapOptions>(
            configuration.GetSection(DistributedTransactionCapOptions.SectionName));

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddScoped<IDistributedTransactionManager, DistributedTransactionManager>();
        services.AddScoped<ITransactionLogger, PersistentTransactionLogger>();
        services.AddScoped<ITransactionCompensator, PersistentTransactionCompensator>();
        services.AddSingleton<ICapTopicNameProvider, DefaultCapTopicNameProvider>();
        services.AddScoped<IDistributedEventPublisher, CapDistributedEventPublisher>();
        services.AddScoped<IEventBus, CapEventBus>();

        services.AddCap(capOptions =>
        {
            capOptions.DefaultGroupName = options.DefaultGroup;
            capOptions.FailedRetryCount = options.FailedRetryCount;
            capOptions.FailedRetryInterval = options.FailedRetryIntervalSeconds;
            capOptions.Version = "v1";

            ConfigureStorage(capOptions, options);
            ConfigureTransport(capOptions, options);

            if (options.UseDashboard)
            {
                capOptions.UseDashboard();
            }
        });

        // Background retry service (optional)
        if (options.EnableCompensationBackgroundWorker)
        {
            services.AddHostedService<CompensationRetryBackgroundService>();
        }

        return services;
    }

    private static void ConfigureStorage(
        DotNetCore.CAP.CapOptions capOptions,
        DistributedTransactionCapOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.StorageConnectionString);

        switch (options.StorageProvider)
        {
            case CapStorageProvider.SqlServer:
                capOptions.UseSqlServer(options.StorageConnectionString);
                break;
            case CapStorageProvider.PostgreSql:
                capOptions.UsePostgreSql(options.StorageConnectionString);
                break;
            default:
                throw new NotSupportedException($"不支持的 CAP 存储提供程序: {options.StorageProvider}");
        }
    }

    private static void ConfigureTransport(
        DotNetCore.CAP.CapOptions capOptions,
        DistributedTransactionCapOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TransportConnectionString);

        switch (options.TransportProvider)
        {
            case CapTransportProvider.RabbitMQ:
                capOptions.UseRabbitMQ(options.TransportConnectionString);
                break;
            case CapTransportProvider.Kafka:
                capOptions.UseKafka(options.TransportConnectionString);
                break;
            default:
                throw new NotSupportedException($"不支持的 CAP 消息传输提供程序: {options.TransportProvider}");
        }
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build framework/src/CrestCreates.DistributedTransaction.CAP/CrestCreates.DistributedTransaction.CAP.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.DistributedTransaction.CAP/Extensions/DistributedTransactionCapServiceCollectionExtensions.cs
git commit -m "feat(DistributedTransaction.CAP): register persistent implementations and background service"
```

---

## Task 9: Source Generator - Model

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorModel.cs`

- [ ] **Step 1: Write model**

```csharp
// framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorModel.cs
using System.Collections.Generic;

namespace CrestCreates.CodeGenerator.CompensationExecutorGenerator
{
    internal sealed class CompensationExecutorModel
    {
        public string Namespace { get; set; } = string.Empty;
        public List<ExecutorInfo> Executors { get; set; } = new();
    }

    internal sealed class ExecutorInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string NameProperty { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorModel.cs
git commit -m "feat(CodeGenerator): add CompensationExecutorModel"
```

---

## Task 10: Source Generator - Code Writer

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorCodeWriter.cs`

- [ ] **Step 1: Write code writer**

```csharp
// framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorCodeWriter.cs
using System.Linq;
using System.Text;

namespace CrestCreates.CodeGenerator.CompensationExecutorGenerator
{
    internal sealed class CompensationExecutorCodeWriter
    {
        public string WriteRegistry(CompensationExecutorModel model)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using CrestCreates.DistributedTransaction.Abstractions;");
            sb.AppendLine();

            sb.AppendLine($"namespace {model.Namespace}");
            sb.AppendLine("{");

            WriteRegistryClass(sb, model);
            sb.AppendLine();
            WriteExtensionsClass(sb, model);

            sb.AppendLine("}");

            return sb.ToString();
        }

        private void WriteRegistryClass(StringBuilder sb, CompensationExecutorModel model)
        {
            sb.AppendLine("    public class CompensationExecutorRegistry : ICompensationExecutorRegistry");
            sb.AppendLine("    {");

            // Fields
            foreach (var executor in model.Executors)
            {
                sb.AppendLine($"        private readonly {executor.FullName} _{CamelCase(executor.ClassName)};");
            }

            sb.AppendLine();

            // Constructor
            sb.AppendLine("        public CompensationExecutorRegistry(");
            var constructorParams = model.Executors
                .Select(e => $"            {e.FullName} {CamelCase(e.ClassName)}")
                .ToList();
            sb.AppendLine(string.Join(",\n", constructorParams));
            sb.AppendLine("        )");
            sb.AppendLine("        {");

            foreach (var executor in model.Executors)
            {
                sb.AppendLine($"            _{CamelCase(executor.ClassName)} = {CamelCase(executor.ClassName)};");
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // GetExecutor method
            sb.AppendLine("        public ICompensationExecutor? GetExecutor(string name) => name switch");
            sb.AppendLine("        {");

            foreach (var executor in model.Executors)
            {
                sb.AppendLine($"            {executor.NameProperty} => _{CamelCase(executor.ClassName)},");
            }

            sb.AppendLine("            _ => null");
            sb.AppendLine("        };");
            sb.AppendLine();

            // GetAll method
            sb.AppendLine("        public IEnumerable<ICompensationExecutor> GetAll() => new ICompensationExecutor[]");
            sb.AppendLine("        {");

            foreach (var executor in model.Executors)
            {
                sb.AppendLine($"            _{CamelCase(executor.ClassName)},");
            }

            sb.AppendLine("        };");

            sb.AppendLine("    }");
        }

        private void WriteExtensionsClass(StringBuilder sb, CompensationExecutorModel model)
        {
            sb.AppendLine("    public static class CompensationExecutorServiceCollectionExtensions");
            sb.AppendLine("    {");
            sb.AppendLine("        public static IServiceCollection AddCompensationExecutors(this IServiceCollection services)");
            sb.AppendLine("        {");

            foreach (var executor in model.Executors)
            {
                sb.AppendLine($"            services.AddScoped<{executor.FullName}>();");
            }

            sb.AppendLine("            services.AddScoped<ICompensationExecutorRegistry, CompensationExecutorRegistry>();");
            sb.AppendLine("            return services;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }

        private static string CamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorCodeWriter.cs
git commit -m "feat(CodeGenerator): add CompensationExecutorCodeWriter"
```

---

## Task 11: Source Generator - Main Generator

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorSourceGenerator.cs`

- [ ] **Step 1: Write source generator**

```csharp
// framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorSourceGenerator.cs
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.CompensationExecutorGenerator
{
    [Generator]
    public sealed class CompensationExecutorSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var executorClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, _) => GetExecutorInfo(ctx))
                .Where(static x => x is not null)
                .Collect();

            context.RegisterSourceOutput(executorClasses, ExecuteGeneration);
        }

        private static bool IsCandidate(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };
        }

        private static ExecutorInfo? GetExecutorInfo(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

            if (symbol == null)
                return null;

            var attribute = symbol.GetAttributes().FirstOrDefault(HasCompensationExecutorAttribute);
            if (attribute == null)
                return null;

            // Find Name property
            var nameProperty = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.Name == "Name");

            var nameValue = nameProperty != null
                ? $"{symbol.Name}.Name"
                : $"\"{symbol.Name.Replace("CompensationExecutor", "").Replace("Executor", "")}\"";

            return new ExecutorInfo
            {
                ClassName = symbol.Name,
                FullName = symbol.ToDisplayString(),
                NameProperty = nameValue
            };
        }

        private static bool HasCompensationExecutorAttribute(AttributeData attr)
        {
            return attr.AttributeClass != null && (
                attr.AttributeClass.Name == "CompensationExecutorAttribute" ||
                attr.AttributeClass.Name == "CompensationExecutor" ||
                attr.AttributeClass.ToDisplayString().EndsWith(".CompensationExecutorAttribute") ||
                attr.AttributeClass.ToDisplayString().EndsWith(".CompensationExecutor"));
        }

        private void ExecuteGeneration(
            SourceProductionContext context,
            ImmutableArray<ExecutorInfo?> executors)
        {
            if (executors.IsDefaultOrEmpty)
                return;

            var validExecutors = executors
                .Where(x => x != null)
                .Cast<ExecutorInfo>()
                .ToList();

            if (validExecutors.Count == 0)
                return;

            // Group by namespace (use first executor's namespace)
            var firstExecutor = validExecutors.First();
            var ns = GetNamespace(firstExecutor.FullName);

            var model = new CompensationExecutorModel
            {
                Namespace = ns,
                Executors = validExecutors
            };

            var writer = new CompensationExecutorCodeWriter();
            var source = writer.WriteRegistry(model);

            context.AddSource(
                "CompensationExecutorRegistry.g.cs",
                SourceText.From(source, System.Text.Encoding.UTF8));
        }

        private static string GetNamespace(string fullName)
        {
            var lastDot = fullName.LastIndexOf('.');
            return lastDot > 0 ? fullName.Substring(0, lastDot) : "Generated";
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/CompensationExecutorGenerator/CompensationExecutorSourceGenerator.cs
git commit -m "feat(CodeGenerator): add CompensationExecutorSourceGenerator"
```

---

## Task 12: Unit Tests

**Files:**
- Create: `framework/test/CrestCreates.CodeGenerator.Tests/CompensationExecutorGenerator/CompensationExecutorSourceGeneratorTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// framework/test/CrestCreates.CodeGenerator.Tests/CompensationExecutorGenerator/CompensationExecutorSourceGeneratorTests.cs
using System.Linq;
using Xunit;
using CrestCreates.CodeGenerator.CompensationExecutorGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;

namespace CrestCreates.CodeGenerator.Tests.CompensationExecutorGenerator
{
    public class CompensationExecutorSourceGeneratorTests
    {
        [Fact]
        public void Should_Generate_Registry_For_Single_Executor()
        {
            var source = @"
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Attributes;

namespace TestNamespace
{
    [CompensationExecutor]
    public class OrderCompensationExecutor : ICompensationExecutor
    {
        public string Name => ""Order"";
        public Task ExecuteAsync(string? data) => Task.CompletedTask;
    }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<CompensationExecutorSourceGenerator>(source);

            Assert.True(result.ContainsFile("CompensationExecutorRegistry.g.cs"));
            var generated = result.GetSourceByFileName("CompensationExecutorRegistry.g.cs");
            Assert.NotNull(generated);
            Assert.Contains("CompensationExecutorRegistry", generated.SourceText);
            Assert.Contains("OrderCompensationExecutor", generated.SourceText);
            Assert.Contains("AddCompensationExecutors", generated.SourceText);
        }

        [Fact]
        public void Should_Generate_Registry_For_Multiple_Executors()
        {
            var source = @"
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Attributes;

namespace TestNamespace
{
    [CompensationExecutor]
    public class OrderCompensationExecutor : ICompensationExecutor
    {
        public string Name => ""Order"";
        public Task ExecuteAsync(string? data) => Task.CompletedTask;
    }

    [CompensationExecutor]
    public class InventoryCompensationExecutor : ICompensationExecutor
    {
        public string Name => ""Inventory"";
        public Task ExecuteAsync(string? data) => Task.CompletedTask;
    }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<CompensationExecutorSourceGenerator>(source);

            var generated = result.GetSourceByFileName("CompensationExecutorRegistry.g.cs");
            Assert.NotNull(generated);
            Assert.Contains("OrderCompensationExecutor", generated.SourceText);
            Assert.Contains("InventoryCompensationExecutor", generated.SourceText);
            Assert.Contains("GetExecutor", generated.SourceText);
            Assert.Contains("GetAll", generated.SourceText);
        }

        [Fact]
        public void Should_Not_Generate_When_No_Executors()
        {
            var source = @"
namespace TestNamespace
{
    public class SomeClass { }
}
";

            var result = SourceGeneratorTestHelper.RunGenerator<CompensationExecutorSourceGenerator>(source);

            Assert.False(result.ContainsFile("CompensationExecutorRegistry.g.cs"));
        }
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CompensationExecutorGenerator" -v q`
Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.CodeGenerator.Tests/CompensationExecutorGenerator/CompensationExecutorSourceGeneratorTests.cs
git commit -m "test(CodeGenerator): add CompensationExecutorSourceGenerator tests"
```

---

## Task 13: Final Verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build framework/CrestCreates.Framework.sln`
Expected: Build succeeded

- [ ] **Step 2: Run all tests**

Run: `dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj`
Expected: All tests pass

- [ ] **Step 3: Final commit (if any changes)**

```bash
git status
# If clean, no commit needed
```

---

## Acceptance Criteria Verification

| Criteria | Task |
|----------|------|
| TransactionLog entity created | Task 1 |
| TransactionCompensation entity created | Task 1 |
| PersistentTransactionLogger implements ITransactionLogger | Task 4 |
| PersistentTransactionCompensator implements ITransactionCompensator | Task 5 |
| CompensationExecutorGenerator generates Registry | Task 11 |
| CompensationRetryBackgroundService handles retry | Task 6 |
| All code AOT-friendly (no reflection) | All tasks |
| Unit tests cover generator | Task 12 |
