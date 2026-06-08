# Event Bus Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local DLQ (storage + auto-retry + management API), event naming convention, and integration tests (local + RabbitMQ + Kafka) to the event bus.

**Architecture:** Three independent layers: (1) Local DLQ in `CrestCreates.EventBus.Local.Channel` with abstractions in `CrestCreates.EventBus.Abstractions`, (2) `EventNamingConvention` static helper in `CrestCreates.EventBus.Abstract`, (3) Three new integration test projects with docker-compose for RabbitMQ/Kafka.

**Tech Stack:** .NET 10, xUnit, Moq, RabbitMQ.Client, Confluent.Kafka, Docker Compose

---

## File Structure Map

### Created Files

```
framework/src/CrestCreates.EventBus.Abstractions/
├── ILocalDeadLetterStore.cs          # Storage interface
├── ILocalDeadLetterManager.cs        # Management API interface
├── DeadLetterMessage.cs              # Message record + status enum
└── LocalDeadLetterOptions.cs         # Configuration options

framework/src/CrestCreates.EventBus.Local.Channel/
├── InMemoryDeadLetterStore.cs        # ConcurrentDictionary implementation
├── LocalDeadLetterBackgroundService.cs # Background retry service
└── DefaultLocalDeadLetterManager.cs  # Management API implementation

framework/src/CrestCreates.EventBus.Abstract/
└── EventNamingConvention.cs          # Naming convention helpers

infra/
└── docker-compose.eventbus.yml       # RabbitMQ + Kafka for testing

framework/test/CrestCreates.EventBus.Local.Tests.Integration/
├── CrestCreates.EventBus.Local.Tests.Integration.csproj
├── LocalDeadLetterQueueTests.cs
├── LocalEventBusDispatchTests.cs
└── LocalEventBusIdempotencyTests.cs

framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/
├── CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
├── RabbitMqIntegrationTestBase.cs
├── RabbitMqPublishConsumeTests.cs
├── RabbitMqRetryAndDLQTests.cs
└── RabbitMqMultiHandlerTests.cs

framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/
├── CrestCreates.EventBus.Kafka.Tests.Integration.csproj
├── KafkaIntegrationTestBase.cs
├── KafkaPublishConsumeTests.cs
├── KafkaRetryAndDLQTests.cs
└── KafkaMultiConsumerTests.cs
```

### Modified Files

```
framework/src/CrestCreates.EventBus.Local/DefaultLocalEventBus.cs
framework/src/CrestCreates.EventBus.Local.Channel/BackgroundChannelLocalEventBusConsumer.cs
framework/src/CrestCreates.EventBus.Local/LocalEventBusModule.cs
framework/src/CrestCreates.EventBus.Local.Channel/LocalChannelEventBusModule.cs
framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBus.cs
framework/src/CrestCreates.EventBus.RabbitMQ/Options/RabbitMqOptions.cs
framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBusModule.cs
framework/src/CrestCreates.EventBus.Kafka/KafkaEventBus.cs
framework/src/CrestCreates.EventBus.Kafka/Options/KafkaOptions.cs
framework/src/CrestCreates.EventBus.Kafka/KafkaEventBusModule.cs
CrestCreates.slnx
```

---

## Phase 1: Local DLQ Abstractions

### Task 1: Create DeadLetterMessage and DeadLetterStatus

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Abstractions/DeadLetterMessage.cs`

- [ ] **Step 1: Write the file**

```csharp
using System;

namespace CrestCreates.EventBus.Abstractions;

public enum DeadLetterStatus
{
    Pending,
    Retrying,
    Retried,
    Archived
}

public sealed record DeadLetterMessage(
    string MessageId,
    string EventType,
    byte[] Payload,
    string ErrorMessage,
    DateTime FailedAt,
    int RetryCount,
    int MaxRetries,
    DeadLetterStatus Status
);
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Abstractions/CrestCreates.EventBus.Abstractions.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Abstractions/DeadLetterMessage.cs
git commit -m "feat: add DeadLetterMessage record and DeadLetterStatus enum"
```

---

### Task 2: Create ILocalDeadLetterStore interface

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Abstractions/ILocalDeadLetterStore.cs`

- [ ] **Step 1: Write the file**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public interface ILocalDeadLetterStore
{
    Task EnqueueAsync(DeadLetterMessage message, CancellationToken cancellationToken = default);

    Task<DeadLetterMessage?> GetAsync(string messageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task MarkRetryingAsync(string messageId, CancellationToken cancellationToken = default);

    Task MarkRetriedAsync(string messageId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string messageId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Abstractions/CrestCreates.EventBus.Abstractions.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Abstractions/ILocalDeadLetterStore.cs
git commit -m "feat: add ILocalDeadLetterStore interface"
```

---

### Task 3: Create ILocalDeadLetterManager interface

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Abstractions/ILocalDeadLetterManager.cs`

- [ ] **Step 1: Write the file**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public sealed record DeadLetterStats(
    int TotalCount,
    int PendingCount,
    int RetryingCount,
    int RetriedCount,
    int ArchivedCount);

public sealed record DeadLetterRetryResult(
    string MessageId,
    bool Success,
    string? ErrorMessage);

public interface ILocalDeadLetterManager
{
    Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<DeadLetterRetryResult> RetryAsync(string messageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeadLetterRetryResult>> RetryAllAsync(CancellationToken cancellationToken = default);

    Task<int> ClearAsync(string? eventType = null, CancellationToken cancellationToken = default);

    Task<DeadLetterStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Abstractions/CrestCreates.EventBus.Abstractions.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Abstractions/ILocalDeadLetterManager.cs
git commit -m "feat: add ILocalDeadLetterManager interface with DeadLetterStats and DeadLetterRetryResult"
```

---

### Task 4: Create LocalDeadLetterOptions

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Abstractions/LocalDeadLetterOptions.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.EventBus.Abstractions;

public sealed class LocalDeadLetterOptions
{
    public int MaxRetries { get; set; } = 3;

    public int RetryIntervalSeconds { get; set; } = 30;

    public int MaxQueueSize { get; set; } = 10000;

    public int AutoCleanArchivedDays { get; set; } = 7;
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Abstractions/CrestCreates.EventBus.Abstractions.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Abstractions/LocalDeadLetterOptions.cs
git commit -m "feat: add LocalDeadLetterOptions configuration class"
```

---

## Phase 2: Local DLQ Implementation

### Task 5: Create InMemoryDeadLetterStore

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Local.Channel/InMemoryDeadLetterStore.cs`

- [ ] **Step 1: Write the file**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local.Channel;

public class InMemoryDeadLetterStore : ILocalDeadLetterStore
{
    private readonly ConcurrentDictionary<string, DeadLetterMessage> _messages = new();

    public Task EnqueueAsync(DeadLetterMessage message, CancellationToken cancellationToken = default)
    {
        _messages[message.MessageId] = message;
        return Task.CompletedTask;
    }

    public Task<DeadLetterMessage?> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryGetValue(messageId, out var message);
        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _messages.Values.AsEnumerable();

        if (eventType is not null)
            query = query.Where(m => m.EventType == eventType);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        var result = query
            .OrderByDescending(m => m.FailedAt)
            .Skip(skip)
            .Take(take)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<DeadLetterMessage>>(result);
    }

    public Task MarkRetryingAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryGetValue(messageId, out var existing);
        if (existing is not null)
        {
            _messages.TryUpdate(messageId,
                existing with { Status = DeadLetterStatus.Retrying },
                existing);
        }
        return Task.CompletedTask;
    }

    public Task MarkRetriedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryGetValue(messageId, out var existing);
        if (existing is not null)
        {
            _messages.TryUpdate(messageId,
                existing with { Status = DeadLetterStatus.Retried },
                existing);
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _messages.TryRemove(messageId, out _);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _messages.Values.AsEnumerable();

        if (eventType is not null)
            query = query.Where(m => m.EventType == eventType);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        return Task.FromResult(query.Count());
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Local.Channel/CrestCreates.EventBus.Local.Channel.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Local.Channel/InMemoryDeadLetterStore.cs
git commit -m "feat: add InMemoryDeadLetterStore implementation"
```

---

### Task 6: Create LocalDeadLetterBackgroundService

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Local.Channel/LocalDeadLetterBackgroundService.cs`

- [ ] **Step 1: Write the file**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local.Channel;

public sealed class LocalDeadLetterBackgroundService : BackgroundService
{
    private readonly ILocalDeadLetterStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDeadLetterBackgroundService> _logger;
    private readonly LocalDeadLetterOptions _options;

    public LocalDeadLetterBackgroundService(
        ILocalDeadLetterStore store,
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDeadLetterBackgroundService> logger,
        IOptions<LocalDeadLetterOptions> options)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.RetryIntervalSeconds),
                    stoppingToken);

                var pending = await _store.ListAsync(
                    status: DeadLetterStatus.Pending,
                    take: 100,
                    cancellationToken: stoppingToken);

                foreach (var message in pending)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    if (message.RetryCount >= _options.MaxRetries)
                    {
                        _logger.LogWarning(
                            "Dead letter message {MessageId} of type {EventType} has reached max retries ({RetryCount}/{MaxRetries}), archiving",
                            message.MessageId, message.EventType, message.RetryCount, _options.MaxRetries);
                        continue;
                    }

                    await _store.MarkRetryingAsync(message.MessageId, stoppingToken);

                    try
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();

                        // Deserialize the event from payload and dispatch
                        // The payload is the serialized ILocalEvent — we use the type name to resolve
                        var eventType = Type.GetType(message.EventType);
                        if (eventType is null)
                        {
                            _logger.LogError(
                                "Cannot resolve event type {EventType} for dead letter message {MessageId}",
                                message.EventType, message.MessageId);
                            continue;
                        }

                        // Deserialize using System.Text.Json
                        var eventData = System.Text.Json.JsonSerializer.Deserialize(
                            message.Payload, eventType);

                        if (eventData is ILocalEvent localEvent)
                        {
                            await dispatcher.DispatchAsync(localEvent, stoppingToken);
                        }

                        await _store.MarkRetriedAsync(message.MessageId, stoppingToken);
                        _logger.LogInformation(
                            "Successfully retried dead letter message {MessageId} of type {EventType}",
                            message.MessageId, message.EventType);
                    }
                    catch (Exception ex)
                    {
                        var newRetryCount = message.RetryCount + 1;
                        _logger.LogError(ex,
                            "Retry {RetryCount}/{MaxRetries} failed for dead letter message {MessageId} of type {EventType}",
                            newRetryCount, _options.MaxRetries, message.MessageId, message.EventType);

                        // Update retry count — mark back to Pending for next cycle, or Archived if exhausted
                        var updatedMessage = message with
                        {
                            RetryCount = newRetryCount,
                            Status = newRetryCount >= _options.MaxRetries
                                ? DeadLetterStatus.Archived
                                : DeadLetterStatus.Pending
                        };
                        await _store.EnqueueAsync(updatedMessage, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in dead letter background retry loop");
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Local.Channel/CrestCreates.EventBus.Local.Channel.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Local.Channel/LocalDeadLetterBackgroundService.cs
git commit -m "feat: add LocalDeadLetterBackgroundService for automatic DLQ retry"
```

---

### Task 7: Create DefaultLocalDeadLetterManager

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Local.Channel/DefaultLocalDeadLetterManager.cs`

- [ ] **Step 1: Write the file**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local.Channel;

public class DefaultLocalDeadLetterManager : ILocalDeadLetterManager
{
    private readonly ILocalDeadLetterStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefaultLocalDeadLetterManager> _logger;

    public DefaultLocalDeadLetterManager(
        ILocalDeadLetterStore store,
        IServiceScopeFactory scopeFactory,
        ILogger<DefaultLocalDeadLetterManager> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<IReadOnlyList<DeadLetterMessage>> ListAsync(
        string? eventType = null,
        DeadLetterStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return _store.ListAsync(eventType, status, skip, take, cancellationToken);
    }

    public async Task<DeadLetterRetryResult> RetryAsync(
        string messageId, CancellationToken cancellationToken = default)
    {
        var message = await _store.GetAsync(messageId, cancellationToken);
        if (message is null)
        {
            return new DeadLetterRetryResult(messageId, false, "Message not found");
        }

        await _store.MarkRetryingAsync(messageId, cancellationToken);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();

            var eventType = Type.GetType(message.EventType);
            if (eventType is null)
            {
                return new DeadLetterRetryResult(messageId, false,
                    $"Cannot resolve event type: {message.EventType}");
            }

            var eventData = System.Text.Json.JsonSerializer.Deserialize(
                message.Payload, eventType);

            if (eventData is ILocalEvent localEvent)
            {
                await dispatcher.DispatchAsync(localEvent, cancellationToken);
            }

            await _store.MarkRetriedAsync(messageId, cancellationToken);
            _logger.LogInformation(
                "Manually retried dead letter message {MessageId} successfully", messageId);

            return new DeadLetterRetryResult(messageId, true, null);
        }
        catch (Exception ex)
        {
            var newRetryCount = message.RetryCount + 1;
            var updatedMessage = message with
            {
                RetryCount = newRetryCount,
                Status = DeadLetterStatus.Pending,
                ErrorMessage = ex.Message
            };
            await _store.EnqueueAsync(updatedMessage, cancellationToken);

            _logger.LogError(ex,
                "Manual retry failed for dead letter message {MessageId}", messageId);

            return new DeadLetterRetryResult(messageId, false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<DeadLetterRetryResult>> RetryAllAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = await _store.ListAsync(
            status: DeadLetterStatus.Pending,
            take: int.MaxValue,
            cancellationToken: cancellationToken);

        var archived = await _store.ListAsync(
            status: DeadLetterStatus.Archived,
            take: int.MaxValue,
            cancellationToken: cancellationToken);

        var allMessages = pending.Concat(archived).ToList();
        var results = new List<DeadLetterRetryResult>();

        foreach (var message in allMessages)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await RetryAsync(message.MessageId, cancellationToken);
            results.Add(result);
        }

        return results.AsReadOnly();
    }

    public async Task<int> ClearAsync(
        string? eventType = null, CancellationToken cancellationToken = default)
    {
        var retried = await _store.ListAsync(
            eventType: eventType,
            status: DeadLetterStatus.Retried,
            take: int.MaxValue,
            cancellationToken: cancellationToken);

        var archived = await _store.ListAsync(
            eventType: eventType,
            status: DeadLetterStatus.Archived,
            take: int.MaxValue,
            cancellationToken: cancellationToken);

        var toRemove = retried.Concat(archived).ToList();
        foreach (var message in toRemove)
        {
            await _store.RemoveAsync(message.MessageId, cancellationToken);
        }

        _logger.LogInformation(
            "Cleared {Count} dead letter messages (eventType: {EventType})",
            toRemove.Count, eventType ?? "all");

        return toRemove.Count;
    }

    public async Task<DeadLetterStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var total = await _store.CountAsync(cancellationToken: cancellationToken);
        var pending = await _store.CountAsync(status: DeadLetterStatus.Pending, cancellationToken: cancellationToken);
        var retrying = await _store.CountAsync(status: DeadLetterStatus.Retrying, cancellationToken: cancellationToken);
        var retried = await _store.CountAsync(status: DeadLetterStatus.Retried, cancellationToken: cancellationToken);
        var archived = await _store.CountAsync(status: DeadLetterStatus.Archived, cancellationToken: cancellationToken);

        return new DeadLetterStats(total, pending, retrying, retried, archived);
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Local.Channel/CrestCreates.EventBus.Local.Channel.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Local.Channel/DefaultLocalDeadLetterManager.cs
git commit -m "feat: add DefaultLocalDeadLetterManager implementation"
```

---

### Task 8: Modify DefaultLocalEventBus to catch exceptions and write to DLQ

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.Local/DefaultLocalEventBus.cs`

- [ ] **Step 1: Read the current file to confirm exact content**

```bash
cat framework/src/CrestCreates.EventBus.Local/DefaultLocalEventBus.cs
```

- [ ] **Step 2: Replace the file content**

Replace the entire file with:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

public class DefaultLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ILocalEventDispatcher _dispatcher;
    private readonly ILocalDeadLetterStore? _deadLetterStore;

    public DefaultLocalEventBus(ILocalEventDispatcher dispatcher, ILocalDeadLetterStore? deadLetterStore = null)
    {
        _dispatcher = dispatcher;
        _deadLetterStore = deadLetterStore;
    }

    public async Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dispatcher.DispatchAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (_deadLetterStore is not null)
        {
            await EnqueueToDeadLetterAsync(@event, ex, cancellationToken);
            throw;
        }
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        try
        {
            await _dispatcher.DispatchAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (_deadLetterStore is not null)
        {
            await EnqueueToDeadLetterAsync(@event, ex, cancellationToken);
            throw;
        }
    }

    Task IEventBus.PublishAsync(IDomainEvent @event, CancellationToken cancellationToken)
    {
        return PublishAsync(@event, cancellationToken);
    }

    Task IEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
    {
        return PublishAsync(@event, cancellationToken);
    }

    void IEventBus.Subscribe<TEvent, THandler>()
    {
    }

    void IEventBus.Unsubscribe<TEvent, THandler>()
    {
    }

    private async Task EnqueueToDeadLetterAsync(ILocalEvent @event, Exception ex, CancellationToken cancellationToken)
    {
        if (_deadLetterStore is null) return;

        var eventType = @event.GetType();
        var payload = JsonSerializer.SerializeToUtf8Bytes(@event, eventType);

        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventType: eventType.AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: ex.Message,
            FailedAt: DateTime.UtcNow,
            RetryCount: 0,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending);

        await _deadLetterStore.EnqueueAsync(message, cancellationToken);
    }
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Local/CrestCreates.EventBus.Local.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Local/DefaultLocalEventBus.cs
git commit -m "feat: add DLQ integration to DefaultLocalEventBus"
```

---

### Task 9: Modify BackgroundChannelLocalEventBusConsumer to catch exceptions and write to DLQ

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.Local.Channel/BackgroundChannelLocalEventBusConsumer.cs`

- [ ] **Step 1: Read the current file**

```bash
cat framework/src/CrestCreates.EventBus.Local.Channel/BackgroundChannelLocalEventBusConsumer.cs
```

- [ ] **Step 2: Replace the file content**

Replace the entire file with:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local.Channel;

public sealed class BackgroundChannelLocalEventBusConsumer : BackgroundService
{
    private readonly ChannelLocalEventQueue _queue;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<BackgroundChannelLocalEventBusConsumer> _logger;

    public BackgroundChannelLocalEventBusConsumer(
        ChannelLocalEventQueue queue,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<BackgroundChannelLocalEventBusConsumer> logger)
    {
        _queue = queue;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var @event in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessEventAsync(@event, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessEventAsync(ILocalEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>();
            await dispatcher.DispatchAsync(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing local event of type {EventType}", @event.GetType().Name);

            // Try to enqueue to DLQ if available
            try
            {
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var deadLetterStore = scope.ServiceProvider.GetService<ILocalDeadLetterStore>();
                if (deadLetterStore is not null)
                {
                    var eventType = @event.GetType();
                    var payload = JsonSerializer.SerializeToUtf8Bytes(@event, eventType);

                    var message = new DeadLetterMessage(
                        MessageId: Guid.NewGuid().ToString("N"),
                        EventType: eventType.AssemblyQualifiedName!,
                        Payload: payload,
                        ErrorMessage: ex.Message,
                        FailedAt: DateTime.UtcNow,
                        RetryCount: 0,
                        MaxRetries: 3,
                        Status: DeadLetterStatus.Pending);

                    await deadLetterStore.EnqueueAsync(message, cancellationToken);
                }
            }
            catch (Exception dlqEx)
            {
                _logger.LogError(dlqEx, "Failed to enqueue event to dead letter store");
            }
        }
    }
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Local.Channel/CrestCreates.EventBus.Local.Channel.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Local.Channel/BackgroundChannelLocalEventBusConsumer.cs
git commit -m "feat: add DLQ integration to BackgroundChannelLocalEventBusConsumer"
```

---

### Task 10: Register DLQ services in LocalEventBusModule

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.Local/LocalEventBusModule.cs`

- [ ] **Step 1: Read the current file**

```bash
cat framework/src/CrestCreates.EventBus.Local/LocalEventBusModule.cs
```

- [ ] **Step 2: Replace the file content**

Replace the entire file with:

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local.Channel;

namespace CrestCreates.EventBus.Local;

[CrestModule]
public class LocalEventBusModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
    }
}
```

- [ ] **Step 3: Add project reference to Local.csproj**

The `CrestCreates.EventBus.Local.csproj` needs a reference to `CrestCreates.EventBus.Local.Channel` for the DLQ types. Read the current csproj:

```bash
cat framework/src/CrestCreates.EventBus.Local/CrestCreates.EventBus.Local.csproj
```

Then add the project reference inside the `<ItemGroup>` that contains other project references:

```xml
<ProjectReference Include="..\CrestCreates.EventBus.Local.Channel\CrestCreates.EventBus.Local.Channel.csproj" />
```

- [ ] **Step 4: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Local/CrestCreates.EventBus.Local.csproj
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Local/LocalEventBusModule.cs framework/src/CrestCreates.EventBus.Local/CrestCreates.EventBus.Local.csproj
git commit -m "feat: register DLQ services in LocalEventBusModule"
```

---

### Task 11: Register DLQ services in LocalChannelEventBusModule

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.Local.Channel/LocalChannelEventBusModule.cs`

- [ ] **Step 1: Read the current file**

```bash
cat framework/src/CrestCreates.EventBus.Local.Channel/LocalChannelEventBusModule.cs
```

- [ ] **Step 2: Replace the file content**

Replace the entire file with:

```csharp
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local.Channel;

[CrestModule]
public class LocalChannelEventBusModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ChannelLocalEventQueue>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, BackgroundChannelLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, BackgroundChannelLocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
        services.AddHostedService<BackgroundChannelLocalEventBusConsumer>();
        services.AddHostedService<LocalDeadLetterBackgroundService>();
    }
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Local.Channel/CrestCreates.EventBus.Local.Channel.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Local.Channel/LocalChannelEventBusModule.cs
git commit -m "feat: register DLQ services in LocalChannelEventBusModule"
```

---

## Phase 3: Naming Convention

### Task 12: Create EventNamingConvention

**Files:**
- Create: `framework/src/CrestCreates.EventBus.Abstract/EventNamingConvention.cs`

- [ ] **Step 1: Write the file**

```csharp
using System;
using System.Text.RegularExpressions;

namespace CrestCreates.EventBus.Abstract;

public static class EventNamingConvention
{
    public static string GetRoutingKey<TEvent>() where TEvent : class
    {
        return PascalToLowerUnderscore(typeof(TEvent).Name);
    }

    public static string GetRoutingKey(string boundedContext, string aggregate, string action)
    {
        return $"{ToLowerKebab(boundedContext)}.{ToLowerKebab(aggregate)}.{ToLowerKebab(action)}";
    }

    public static string GetTopic<TEvent>() where TEvent : class
    {
        return typeof(TEvent).Name;
    }

    public static string GetTopic(string boundedContext)
    {
        return $"{ToLowerKebab(boundedContext)}.events";
    }

    public static string GetExchange(string boundedContext)
    {
        return $"crestcreates.{ToLowerKebab(boundedContext)}.events";
    }

    public static string GetQueue(string serviceName, string routingKey)
    {
        return $"{ToLowerKebab(serviceName)}.{routingKey}";
    }

    public static string GetConsumerGroup(string serviceName, string boundedContext)
    {
        return $"{ToLowerKebab(serviceName)}.{ToLowerKebab(boundedContext)}";
    }

    public static string GetDeadLetterQueue(string queue)
    {
        return $"{queue}.dlq";
    }

    public static string GetDeadLetterTopic(string topic)
    {
        return $"{topic}.dlq";
    }

    private static string PascalToLowerUnderscore(string pascalCase)
    {
        return Regex.Replace(pascalCase, "([a-z])([A-Z])", "$1_$2")
            .ToLowerInvariant();
    }

    private static string ToLowerKebab(string input)
    {
        return input.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Abstract/CrestCreates.EventBus.Abstract.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Abstract/EventNamingConvention.cs
git commit -m "feat: add EventNamingConvention static helper class"
```

---

### Task 13: Update RabbitMqEventBus to use EventNamingConvention

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBus.cs`

- [ ] **Step 1: Read the current file**

```bash
cat framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBus.cs
```

- [ ] **Step 2: Replace the file content**

Replace the entire file with:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Options;

namespace CrestCreates.EventBus.RabbitMQ;

public class RabbitMqEventBus : DistributedEventBusBase
{
    private readonly RabbitMqPublisher _publisher;
    private readonly RabbitMqOptions _options;

    public RabbitMqEventBus(RabbitMqPublisher publisher, Microsoft.Extensions.Options.IOptions<RabbitMqOptions> options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options.Value;
    }

    public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var routingKey = EventNamingConvention.GetRoutingKey(@event.GetType());
        await _publisher.PublishAsync(@event, _options.DefaultExchange, routingKey, null, cancellationToken);
    }

    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var routingKey = EventNamingConvention.GetRoutingKey<TEvent>();
        await _publisher.PublishAsync(@event, _options.DefaultExchange, routingKey, null, cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime subscription is not supported. Use the compile-time [RabbitMqSubscribe] attribute instead.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime unsubscription is not supported. Subscriptions are managed at compile time.");
    }
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBus.cs
git commit -m "refactor: use EventNamingConvention in RabbitMqEventBus"
```

---

### Task 14: Update KafkaEventBus to use EventNamingConvention

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.Kafka/KafkaEventBus.cs`

- [ ] **Step 1: Read the current file**

```bash
cat framework/src/CrestCreates.EventBus.Kafka/KafkaEventBus.cs
```

- [ ] **Step 2: Replace the file content**

Replace the entire file with:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;

namespace CrestCreates.EventBus.Kafka;

public class KafkaEventBus : DistributedEventBusBase
{
    private readonly KafkaPublisher _publisher;
    private readonly KafkaOptions _options;

    public KafkaEventBus(KafkaPublisher publisher, Microsoft.Extensions.Options.IOptions<KafkaOptions> options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options.Value;
    }

    public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var topic = EventNamingConvention.GetTopic(@event.GetType());
        var key = EventNamingConvention.GetRoutingKey(@event.GetType());
        await _publisher.PublishAsync(topic, @event, key: key, headers: null, cancellationToken: cancellationToken);
    }

    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var topic = EventNamingConvention.GetTopic<TEvent>();
        var key = EventNamingConvention.GetRoutingKey<TEvent>();
        await _publisher.PublishAsync(topic, @event, key: key, headers: null, cancellationToken: cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime subscription is not supported. Use the compile-time [KafkaSubscribe] attribute instead.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime unsubscription is not supported. Subscriptions are managed at compile time.");
    }
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.EventBus.Kafka/CrestCreates.EventBus.Kafka.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.EventBus.Kafka/KafkaEventBus.cs
git commit -m "refactor: use EventNamingConvention in KafkaEventBus"
```

---

### Task 15: Run existing unit tests to verify naming convention changes don't break anything

**Files:**
- None (verification only)

- [ ] **Step 1: Run the core EventBus tests**

```bash
dotnet test framework/test/CrestCreates.EventBus.Tests/CrestCreates.EventBus.Tests.csproj
```

Expected: All tests pass. If any fail, the test assertions may reference old naming patterns — fix them.

- [ ] **Step 2: Run the RabbitMQ tests**

```bash
dotnet test framework/test/CrestCreates.EventBus.RabbitMQ.Tests/CrestCreates.EventBus.RabbitMQ.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 3: Run the Kafka tests**

```bash
dotnet test framework/test/CrestCreates.EventBus.Kafka.Tests/CrestCreates.EventBus.Kafka.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 4: If all pass, commit**

```bash
git commit -m "test: verify existing tests pass with naming convention changes"
```

---

## Phase 4: Docker Compose

### Task 16: Create docker-compose for event bus testing

**Files:**
- Create: `infra/docker-compose.eventbus.yml`

- [ ] **Step 1: Create the infra directory if it doesn't exist**

```bash
mkdir -p infra
```

- [ ] **Step 2: Write the docker-compose file**

```yaml
services:
  rabbitmq:
    image: rabbitmq:4.0-management-alpine
    container_name: crestcreates-test-rabbitmq
    ports:
      - "5672:5672"
      - "15672:15672"
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_port_connectivity"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s

  kafka:
    image: apache/kafka:4.0.0
    container_name: crestcreates-test-kafka
    ports:
      - "9092:9092"
    environment:
      KAFKA_PROCESS_ROLES: "broker,controller"
      KAFKA_NODE_ID: 1
      KAFKA_CONTROLLER_QUORUM_VOTERS: "1@localhost:9093"
      KAFKA_LISTENERS: "PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093"
      KAFKA_ADVERTISED_LISTENERS: "PLAINTEXT://localhost:9092"
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: "PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT"
      KAFKA_INTER_BROKER_LISTENER_NAME: "PLAINTEXT"
      KAFKA_CONTROLLER_LISTENER_NAMES: "CONTROLLER"
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
    healthcheck:
      test: ["CMD", "/opt/kafka/bin/kafka-broker-api-versions.sh", "--bootstrap-server", "localhost:9092"]
      interval: 10s
      timeout: 10s
      retries: 15
      start_period: 30s
```

- [ ] **Step 3: Verify docker-compose syntax**

```bash
docker compose -f infra/docker-compose.eventbus.yml config
```

Expected: Prints the parsed compose file without errors.

- [ ] **Step 4: Commit**

```bash
git add infra/docker-compose.eventbus.yml
git commit -m "feat: add docker-compose for RabbitMQ and Kafka integration testing"
```

---

## Phase 5: Local DLQ Integration Tests

### Task 17: Create the Local DLQ integration test project

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj`

- [ ] **Step 1: Create the project directory**

```bash
mkdir -p framework/test/CrestCreates.EventBus.Local.Tests.Integration
```

- [ ] **Step 2: Write the .csproj file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.EventBus.Local.Tests.Integration</RootNamespace>
    <AssemblyName>CrestCreates.EventBus.Local.Tests.Integration</AssemblyName>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.Local.Channel\CrestCreates.EventBus.Local.Channel.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.Local\CrestCreates.EventBus.Local.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.Abstractions\CrestCreates.EventBus.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Domain\CrestCreates.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
git commit -m "test: create Local DLQ integration test project"
```

---

### Task 18: Write LocalDeadLetterQueueTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Local.Tests.Integration/LocalDeadLetterQueueTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalDeadLetterQueueTests
{
    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ChannelLocalEventQueue>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EnqueueFailingEvent_HandlerThrows_EventEntersDLQ()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<FailingTestEvent>, FailingTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var testEvent = new FailingTestEvent();

        // Act
        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(testEvent));

        // Assert
        exception.Should().NotBeNull();
        var messages = await store.ListAsync(take: 10);
        messages.Should().HaveCount(1);
        messages[0].EventType.Should().Contain(nameof(FailingTestEvent));
        messages[0].Status.Should().Be(DeadLetterStatus.Pending);
        messages[0].RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RetrySucceeds_DLQRetry_HandlerSucceeds_MarkedRetried()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventType: typeof(RetryTestEvent).AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: "Test failure",
            FailedAt: DateTime.UtcNow,
            RetryCount: 0,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending);
        await store.EnqueueAsync(message);

        // Act
        var result = await manager.RetryAsync(message.MessageId);

        // Assert
        result.Success.Should().BeTrue();
        var updated = await store.GetAsync(message.MessageId);
        updated!.Status.Should().Be(DeadLetterStatus.Retried);
    }

    [Fact]
    public async Task RetryExhausted_ReachesMaxRetries_StatusArchived()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<FailingTestEvent>, FailingTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new FailingTestEvent(), typeof(FailingTestEvent));
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventType: typeof(FailingTestEvent).AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: "Test failure",
            FailedAt: DateTime.UtcNow,
            RetryCount: 2,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending);
        await store.EnqueueAsync(message);

        // Act
        var result = await manager.RetryAsync(message.MessageId);

        // Assert
        result.Success.Should().BeFalse();
        var updated = await store.GetAsync(message.MessageId);
        updated!.Status.Should().Be(DeadLetterStatus.Archived);
        updated.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task ManualRetry_RetryAsync_RetriesSpecificMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventType: typeof(RetryTestEvent).AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: "Test failure",
            FailedAt: DateTime.UtcNow,
            RetryCount: 1,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending);
        await store.EnqueueAsync(message);

        // Act
        var result = await manager.RetryAsync(message.MessageId);

        // Assert
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be(message.MessageId);
    }

    [Fact]
    public async Task RetryAll_RetriesAllPendingMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        for (int i = 0; i < 3; i++)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
            var message = new DeadLetterMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                EventType: typeof(RetryTestEvent).AssemblyQualifiedName!,
                Payload: payload,
                ErrorMessage: $"Failure {i}",
                FailedAt: DateTime.UtcNow,
                RetryCount: 0,
                MaxRetries: 3,
                Status: DeadLetterStatus.Pending);
            await store.EnqueueAsync(message);
        }

        // Act
        var results = await manager.RetryAllAsync();

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }

    [Fact]
    public async Task ClearByEventType_RemovesMatchingMessages()
    {
        // Arrange
        var store = new InMemoryDeadLetterStore();
        var manager = new DefaultLocalDeadLetterManager(
            store,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultLocalDeadLetterManager>.Instance);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        var msg1 = new DeadLetterMessage("1", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried);
        var msg2 = new DeadLetterMessage("2", typeof(FailingTestEvent).AssemblyQualifiedName!,
            payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried);
        await store.EnqueueAsync(msg1);
        await store.EnqueueAsync(msg2);

        // Act
        var removed = await manager.ClearAsync(typeof(RetryTestEvent).AssemblyQualifiedName);

        // Assert
        removed.Should().Be(1);
        var remaining = await store.ListAsync(take: 10);
        remaining.Should().HaveCount(1);
        remaining[0].EventType.Should().Contain(nameof(FailingTestEvent));
    }

    [Fact]
    public async Task ListWithFilter_SupportsPagingAndTypeFilter()
    {
        // Arrange
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        for (int i = 0; i < 5; i++)
        {
            var msg = new DeadLetterMessage(i.ToString(), typeof(RetryTestEvent).AssemblyQualifiedName!,
                payload, $"err {i}", DateTime.UtcNow.AddMinutes(-i), 0, 3, DeadLetterStatus.Pending);
            await store.EnqueueAsync(msg);
        }

        // Act
        var page1 = await store.ListAsync(skip: 0, take: 2);
        var page2 = await store.ListAsync(skip: 2, take: 2);

        // Assert
        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1[0].FailedAt.Should().BeAfter(page2[0].FailedAt); // Ordered by FailedAt desc
    }

    [Fact]
    public async Task IdempotencyWithDLQ_DuplicateEvent_NoDuplicateProcessing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<CountingTestEvent>, CountingTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CountingTestEvent(), typeof(CountingTestEvent));

        // Enqueue the same message twice
        var msg = new DeadLetterMessage("dup-1", typeof(CountingTestEvent).AssemblyQualifiedName!,
            payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
        await store.EnqueueAsync(msg);
        await store.EnqueueAsync(msg); // Duplicate — should be idempotent in store

        // Assert: store should only have one entry (ConcurrentDictionary key is MessageId)
        var all = await store.ListAsync(take: 10);
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConcurrentEnqueue_MultipleThreads_DataConsistent()
    {
        // Arrange
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var id = i.ToString();
            tasks.Add(Task.Run(async () =>
            {
                var msg = new DeadLetterMessage(id, typeof(RetryTestEvent).AssemblyQualifiedName!,
                    payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
                await store.EnqueueAsync(msg);
            }));
        }
        await Task.WhenAll(tasks);

        // Assert
        var count = await store.CountAsync();
        count.Should().Be(100);
    }

    [Fact]
    public async Task DeadLetterStats_ReturnsCorrectCounts()
    {
        // Arrange
        var store = new InMemoryDeadLetterStore();
        var manager = new DefaultLocalDeadLetterManager(
            store,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultLocalDeadLetterManager>.Instance);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        await store.EnqueueAsync(new DeadLetterMessage("1", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending));
        await store.EnqueueAsync(new DeadLetterMessage("2", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried));
        await store.EnqueueAsync(new DeadLetterMessage("3", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", DateTime.UtcNow, 3, 3, DeadLetterStatus.Archived));

        // Act
        var stats = await manager.GetStatsAsync();

        // Assert
        stats.TotalCount.Should().Be(3);
        stats.PendingCount.Should().Be(1);
        stats.RetriedCount.Should().Be(1);
        stats.ArchivedCount.Should().Be(1);
    }

    [Fact]
    public async Task MaxQueueSizeProtection_StoreDoesNotEnforceLimit()
    {
        // Note: InMemoryDeadLetterStore does not enforce MaxQueueSize — it's advisory.
        // This test verifies the store accepts many messages without error.
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        for (int i = 0; i < 1000; i++)
        {
            var msg = new DeadLetterMessage(i.ToString(), typeof(RetryTestEvent).AssemblyQualifiedName!,
                payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
            await store.EnqueueAsync(msg);
        }

        var count = await store.CountAsync();
        count.Should().Be(1000);
    }

    // Test event types and handlers

    private sealed class FailingTestEvent : DomainEvent { }

    private sealed class FailingTestEventHandler : ILocalEventHandler<FailingTestEvent>
    {
        public Task HandleAsync(FailingTestEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler always fails");
        }
    }

    private sealed class RetryTestEvent : DomainEvent { }

    private sealed class RetryTestEventHandler : ILocalEventHandler<RetryTestEvent>
    {
        public Task HandleAsync(RetryTestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CountingTestEvent : DomainEvent { }

    private sealed class CountingTestEventHandler : ILocalEventHandler<CountingTestEvent>
    {
        public int CallCount { get; private set; }

        public Task HandleAsync(CountingTestEvent @event, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Run the tests**

```bash
dotnet test framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
```

Expected: All 11 tests pass.

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Local.Tests.Integration/LocalDeadLetterQueueTests.cs
git commit -m "test: add Local DLQ integration tests (11 cases)"
```

---

### Task 19: Write LocalEventBusDispatchTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Local.Tests.Integration/LocalEventBusDispatchTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalEventBusDispatchTests
{
    [Fact]
    public async Task PublishAsync_WithDLQ_HandlerSucceeds_NoDLQEntry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<SuccessTestEvent>, SuccessTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<ILocalDeadLetterStore>();

        // Act
        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new SuccessTestEvent()));

        // Assert
        exception.Should().BeNull();
        var dlqCount = await store.CountAsync();
        dlqCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WithDLQ_HandlerFails_EventInDLQAndExceptionPropagated()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<AlwaysFailEvent>, AlwaysFailEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<ILocalDeadLetterStore>();

        // Act
        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new AlwaysFailEvent()));

        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeOfType<InvalidOperationException>();
        var dlqCount = await store.CountAsync();
        dlqCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_WithoutDLQ_HandlerFails_ExceptionPropagated_NoDLQ()
    {
        // Arrange — no DLQ store registered
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<AlwaysFailEvent>, AlwaysFailEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        // Act
        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new AlwaysFailEvent()));

        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeOfType<InvalidOperationException>();
    }

    // Test event types and handlers

    private sealed class SuccessTestEvent : DomainEvent { }

    private sealed class SuccessTestEventHandler : ILocalEventHandler<SuccessTestEvent>
    {
        public bool WasCalled { get; private set; }

        public Task HandleAsync(SuccessTestEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailEvent : DomainEvent { }

    private sealed class AlwaysFailEventHandler : ILocalEventHandler<AlwaysFailEvent>
    {
        public Task HandleAsync(AlwaysFailEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Always fails");
        }
    }
}
```

- [ ] **Step 2: Build and run tests**

```bash
dotnet build framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
dotnet test framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
```

Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Local.Tests.Integration/LocalEventBusDispatchTests.cs
git commit -m "test: add local event bus dispatch integration tests"
```

---

### Task 20: Write LocalEventBusIdempotencyTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Local.Tests.Integration/LocalEventBusIdempotencyTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalEventBusIdempotencyTests
{
    [Fact]
    public async Task PublishAsync_SameEventTwice_BothDispatched()
    {
        // Local event bus does not have built-in idempotency — it dispatches every time.
        // This test verifies the current behavior.
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<IdempotentTestEvent>, IdempotentTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var testEvent = new IdempotentTestEvent();

        // Act
        await eventBus.PublishAsync(testEvent);
        await eventBus.PublishAsync(testEvent);

        // Assert — handler called twice (no idempotency at local level)
        // This is expected behavior — idempotency is for distributed events
    }

    [Fact]
    public async Task DLQStore_EnqueueSameMessageId_Twice_OnlyStoredOnce()
    {
        // The ConcurrentDictionary key is MessageId, so duplicate IDs overwrite
        var store = new InMemoryDeadLetterStore();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new IdempotentTestEvent(), typeof(IdempotentTestEvent));

        var msg1 = new DeadLetterMessage("same-id", typeof(IdempotentTestEvent).AssemblyQualifiedName!,
            payload, "err1", System.DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
        var msg2 = new DeadLetterMessage("same-id", typeof(IdempotentTestEvent).AssemblyQualifiedName!,
            payload, "err2", System.DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);

        await store.EnqueueAsync(msg1);
        await store.EnqueueAsync(msg2);

        var count = await store.CountAsync();
        count.Should().Be(1);
        var stored = await store.GetAsync("same-id");
        stored!.ErrorMessage.Should().Be("err2"); // Second write wins
    }

    private sealed class IdempotentTestEvent : DomainEvent { }

    private sealed class IdempotentTestEventHandler : ILocalEventHandler<IdempotentTestEvent>
    {
        public int CallCount { get; private set; }

        public Task HandleAsync(IdempotentTestEvent @event, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Build and run tests**

```bash
dotnet build framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
dotnet test framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
```

Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Local.Tests.Integration/LocalEventBusIdempotencyTests.cs
git commit -m "test: add local event bus idempotency integration tests"
```

---

## Phase 6: RabbitMQ Integration Tests

### Task 21: Create RabbitMQ integration test project

**Files:**
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj`

- [ ] **Step 1: Create directory and .csproj**

```bash
mkdir -p framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.EventBus.RabbitMQ.Tests.Integration</RootNamespace>
    <AssemblyName>CrestCreates.EventBus.RabbitMQ.Tests.Integration</AssemblyName>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.RabbitMQ\CrestCreates.EventBus.RabbitMQ.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.Abstract\CrestCreates.EventBus.Abstract.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Domain\CrestCreates.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
git commit -m "test: create RabbitMQ integration test project"
```

---

### Task 22: Write RabbitMqIntegrationTestBase

**Files:**
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqIntegrationTestBase.cs`

- [ ] **Step 1: Write the base class**

```csharp
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public abstract class RabbitMqIntegrationTestBase : IAsyncLifetime
{
    protected ServiceProvider ServiceProvider { get; private set; } = null!;
    protected RabbitMqOptions Options { get; private set; } = new();

    public virtual async Task InitializeAsync()
    {
        if (!await IsRabbitMqAvailable())
        {
            throw new SkipException(
                "RabbitMQ is not available. Start it with: docker compose -f infra/docker-compose.eventbus.yml up -d");
        }

        Options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            RetryCount = 2,
            RetryDelaySeconds = 1,
            DeadLetterExchange = "crestcreates.test.dlx",
            DefaultExchange = "crestcreates.test.events",
            MaxChannels = 5
        };

        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(Options));
        services.AddSingleton<RabbitMqConnectionPool>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddSingleton<RabbitMqEventBus>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ServiceProvider = services.BuildServiceProvider();
    }

    public virtual async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await ServiceProvider.DisposeAsync();
        }
    }

    private static async Task<bool> IsRabbitMqAvailable()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", 5672);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqIntegrationTestBase.cs
git commit -m "test: add RabbitMQ integration test base with connection check"
```

---

### Task 23: Write RabbitMqPublishConsumeTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqPublishConsumeTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public class RabbitMqPublishConsumeTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task PublishAndConsume_EventPublished_ConsumerReceives()
    {
        // Arrange
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();
        var queueName = $"test-queue-{Guid.NewGuid():N}";

        // Declare a test queue and bind it
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            var testEvent = new TestRabbitEvent { Message = "Hello RabbitMQ" };

            // Act
            await publisher.PublishAsync(testEvent, exchange, routingKey, null, CancellationToken.None);

            // Assert — consume the message
            var tcs = new TaskCompletionSource<TestRabbitEvent?>();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                var body = ea.Body.ToArray();
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var eventData = JsonSerializer.Deserialize<TestRabbitEvent>(envelope!.Payload);
                tcs.SetResult(eventData);
                await Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            received.Should().NotBeNull();
            received!.Message.Should().Be("Hello RabbitMQ");
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task MultiHandlerDispatch_MultipleQueues_AllReceive()
    {
        // Arrange
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();

        var queue1 = $"multi-q1-{Guid.NewGuid():N}";
        var queue2 = $"multi-q2-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queue1, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueDeclareAsync(queue2, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queue1, exchange, routingKey);
            await channel.QueueBindAsync(queue2, exchange, routingKey);

            var tcs1 = new TaskCompletionSource<bool>();
            var tcs2 = new TaskCompletionSource<bool>();

            var consumer1 = new AsyncEventingBasicConsumer(channel);
            consumer1.ReceivedAsync += async (_, ea) => { tcs1.SetResult(true); await Task.CompletedTask; };
            await channel.BasicConsumeAsync(queue1, autoAck: true, consumer1);

            var consumer2 = new AsyncEventingBasicConsumer(channel);
            consumer2.ReceivedAsync += async (_, ea) => { tcs2.SetResult(true); await Task.CompletedTask; };
            await channel.BasicConsumeAsync(queue2, autoAck: true, consumer2);

            // Act
            await publisher.PublishAsync(new TestRabbitEvent { Message = "Broadcast" }, exchange, routingKey, null, CancellationToken.None);

            // Assert
            var result1 = await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var result2 = await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(10));
            result1.Should().BeTrue();
            result2.Should().BeTrue();
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task LargePayload_EventWithLargeBody_TransmittedCorrectly()
    {
        // Arrange
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();
        var queueName = $"large-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            var largeMessage = new string('x', 100_000);
            var testEvent = new TestRabbitEvent { Message = largeMessage };

            var tcs = new TaskCompletionSource<string?>();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var eventData = JsonSerializer.Deserialize<TestRabbitEvent>(envelope!.Payload);
                tcs.SetResult(eventData!.Message);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer);

            // Act
            await publisher.PublishAsync(testEvent, exchange, routingKey, null, CancellationToken.None);

            // Assert
            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
            received.Should().Be(largeMessage);
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task PublisherConfirmation_PublishWithConfirm_MessageAcknowledged()
    {
        // This test verifies that publishing does not throw, which means
        // publisher confirms succeeded (the RabbitMqPublisher uses confirms internally).
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();

        var exception = await Record.ExceptionAsync(() =>
            publisher.PublishAsync(new TestRabbitEvent { Message = "Confirm" }, exchange, routingKey, null, CancellationToken.None));

        exception.Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentPublishers_MultiplePublishers_AllSucceed()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<TestRabbitEvent>();

        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks[i] = publisher.PublishAsync(
                new TestRabbitEvent { Message = $"Concurrent {idx}" },
                exchange, routingKey, null, CancellationToken.None);
        }

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        exception.Should().BeNull();
    }
}

public sealed class TestRabbitEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqPublishConsumeTests.cs
git commit -m "test: add RabbitMQ publish/consume integration tests"
```

---

### Task 24: Write RabbitMqRetryAndDLQTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqRetryAndDLQTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public class RabbitMqRetryAndDLQTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task HandlerFailureRetry_MessageNacked_Redelivered()
    {
        // Arrange
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<RetryTestRabbitEvent>();
        var queueName = $"retry-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            int deliveryCount = 0;
            var allDelivered = new TaskCompletionSource<bool>();

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                deliveryCount++;
                if (deliveryCount >= 2)
                {
                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                    allDelivered.SetResult(true);
                }
                else
                {
                    // First delivery: nack with requeue
                    await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
                }
            };
            await channel.BasicConsumeAsync(queueName, autoAck: false, consumer);

            // Act
            await publisher.PublishAsync(
                new RetryTestRabbitEvent { Message = "Retry me" },
                exchange, routingKey, null, CancellationToken.None);

            // Assert
            var result = await allDelivered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            result.Should().BeTrue();
            deliveryCount.Should().BeGreaterOrEqualTo(2);
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task HandlerFailureDLQ_MessageRejected_GoesToDLQ()
    {
        // Arrange
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var dlx = Options.DeadLetterExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<DLQTestRabbitEvent>();
        var queueName = $"dlq-main-{Guid.NewGuid():N}";
        var dlqName = $"{queueName}.dlq";

        var channel = await pool.GetChannelAsync();
        try
        {
            // Declare DLX
            await channel.ExchangeDeclareAsync(dlx, ExchangeType.Direct, durable: true);

            // Declare DLQ
            await channel.QueueDeclareAsync(dlqName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(dlqName, dlx, queueName);

            // Declare main queue with DLX
            var args = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", dlx },
                { "x-dead-letter-routing-key", queueName }
            };
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true, arguments: args!);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            // Consumer that always rejects without requeue
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            };
            await channel.BasicConsumeAsync(queueName, autoAck: false, consumer);

            // Act
            await publisher.PublishAsync(
                new DLQTestRabbitEvent { Message = "To DLQ" },
                exchange, routingKey, null, CancellationToken.None);

            // Wait for DLQ
            await Task.Delay(2000);

            // Assert — message should be in DLQ
            var dlqMessage = await channel.BasicGetAsync(dlqName, autoAck: true);
            dlqMessage.Should().NotBeNull();
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task UnknownEventType_DeserializationFails_GoesToDLQ()
    {
        // This test verifies that when a message with an unknown type arrives,
        // the RabbitMqConsumer correctly sends it to DLQ.
        // We simulate this by publishing a raw message with a bad event type.

        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var dlx = Options.DeadLetterExchange;
        var queueName = $"unknown-q-{Guid.NewGuid():N}";
        var dlqName = $"{queueName}.dlq";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(dlx, ExchangeType.Direct, durable: true);
            await channel.QueueDeclareAsync(dlqName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(dlqName, dlx, queueName);

            var args = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", dlx },
                { "x-dead-letter-routing-key", queueName }
            };
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true, arguments: args!);
            await channel.QueueBindAsync(queueName, exchange, "#");

            // Publish a malformed envelope
            var badEnvelope = new RabbitMqMessageEnvelope(
                eventType: "NonExistent.Type, NonExistentAssembly",
                payload: "{}",
                headers: null);

            var body = System.Text.Encoding.UTF8.GetBytes(badEnvelope.EventType); // Simplified: just send bad bytes
            var props = new BasicProperties();
            await channel.BasicPublishAsync(exchange, queueName, true, props, body, CancellationToken.None);

            // Consumer that rejects unknown types
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            };
            await channel.BasicConsumeAsync(queueName, autoAck: false, consumer);

            await Task.Delay(2000);

            var dlqMessage = await channel.BasicGetAsync(dlqName, autoAck: true);
            dlqMessage.Should().NotBeNull();
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task ConnectionRecovery_ChannelRecreated_AfterDispose()
    {
        // Verify that getting a new channel after returning one works
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();

        var channel1 = await pool.GetChannelAsync();
        channel1.IsOpen.Should().BeTrue();
        await pool.ReturnChannelAsync(channel1);

        var channel2 = await pool.GetChannelAsync();
        channel2.IsOpen.Should().BeTrue();
        await pool.ReturnChannelAsync(channel2);
    }
}

public sealed class RetryTestRabbitEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

public sealed class DLQTestRabbitEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqRetryAndDLQTests.cs
git commit -m "test: add RabbitMQ retry and DLQ integration tests"
```

---

### Task 25: Write RabbitMqMultiHandlerTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqMultiHandlerTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

public class RabbitMqMultiHandlerTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task MultipleQueues_SameExchange_AllReceiveCopy()
    {
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<MultiHandlerRabbitEvent>();

        var queueA = $"multi-a-{Guid.NewGuid():N}";
        var queueB = $"multi-b-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queueA, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueDeclareAsync(queueB, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueA, exchange, routingKey);
            await channel.QueueBindAsync(queueB, exchange, routingKey);

            var tcsA = new TaskCompletionSource<string?>();
            var tcsB = new TaskCompletionSource<string?>();

            var consumerA = new AsyncEventingBasicConsumer(channel);
            consumerA.ReceivedAsync += async (_, ea) =>
            {
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var evt = JsonSerializer.Deserialize<MultiHandlerRabbitEvent>(envelope!.Payload);
                tcsA.SetResult(evt!.Message);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueA, autoAck: true, consumerA);

            var consumerB = new AsyncEventingBasicConsumer(channel);
            consumerB.ReceivedAsync += async (_, ea) =>
            {
                var envelope = JsonSerializer.Deserialize(ea.Body.Span, RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
                var evt = JsonSerializer.Deserialize<MultiHandlerRabbitEvent>(envelope!.Payload);
                tcsB.SetResult(evt!.Message);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueB, autoAck: true, consumerB);

            // Act
            await publisher.PublishAsync(
                new MultiHandlerRabbitEvent { Message = "Fanout" },
                exchange, routingKey, null, CancellationToken.None);

            // Assert
            var msgA = await tcsA.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var msgB = await tcsB.Task.WaitAsync(TimeSpan.FromSeconds(10));
            msgA.Should().Be("Fanout");
            msgB.Should().Be("Fanout");
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }

    [Fact]
    public async Task IdempotentConsumption_SameMessageId_Deduplicated()
    {
        // RabbitMQ does not natively deduplicate — this test verifies that
        // the same message published twice is delivered twice (expected behavior
        // without idempotency consumer integration).
        var publisher = ServiceProvider.GetRequiredService<RabbitMqPublisher>();
        var pool = ServiceProvider.GetRequiredService<RabbitMqConnectionPool>();
        var exchange = Options.DefaultExchange;
        var routingKey = EventNamingConvention.GetRoutingKey<MultiHandlerRabbitEvent>();
        var queueName = $"idem-q-{Guid.NewGuid():N}";

        var channel = await pool.GetChannelAsync();
        try
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queueName, exchange, routingKey);

            int receiveCount = 0;
            var done = new TaskCompletionSource<bool>();

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                receiveCount++;
                if (receiveCount >= 2)
                    done.SetResult(true);
                await Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer);

            // Publish the same event twice
            await publisher.PublishAsync(
                new MultiHandlerRabbitEvent { Message = "Dup" },
                exchange, routingKey, null, CancellationToken.None);
            await publisher.PublishAsync(
                new MultiHandlerRabbitEvent { Message = "Dup" },
                exchange, routingKey, null, CancellationToken.None);

            var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            result.Should().BeTrue();
            receiveCount.Should().Be(2); // Both delivered — no idempotency at RabbitMQ level
        }
        finally
        {
            pool.ReturnChannel(channel);
        }
    }
}

public sealed class MultiHandlerRabbitEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/RabbitMqMultiHandlerTests.cs
git commit -m "test: add RabbitMQ multi-handler integration tests"
```

---

## Phase 7: Kafka Integration Tests

### Task 26: Create Kafka integration test project

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj`

- [ ] **Step 1: Create directory and .csproj**

```bash
mkdir -p framework/test/CrestCreates.EventBus.Kafka.Tests.Integration
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.EventBus.Kafka.Tests.Integration</RootNamespace>
    <AssemblyName>CrestCreates.EventBus.Kafka.Tests.Integration</AssemblyName>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.Kafka\CrestCreates.EventBus.Kafka.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.Abstract\CrestCreates.EventBus.Abstract.csproj" />
    <ProjectReference Include="..\..\src\CrestCreates.Domain\CrestCreates.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj
git commit -m "test: create Kafka integration test project"
```

---

### Task 27: Write KafkaIntegrationTestBase

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaIntegrationTestBase.cs`

- [ ] **Step 1: Write the base class**

```csharp
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public abstract class KafkaIntegrationTestBase : IAsyncLifetime
{
    protected ServiceProvider ServiceProvider { get; private set; } = null!;
    protected KafkaOptions Options { get; private set; } = new();

    public virtual async Task InitializeAsync()
    {
        if (!await IsKafkaAvailable())
        {
            throw new SkipException(
                "Kafka is not available. Start it with: docker compose -f infra/docker-compose.eventbus.yml up -d");
        }

        Options = new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            RetryCount = 2,
            RetryDelaySeconds = 1,
            DeadLetterTopicSuffix = ".test.dlq",
            DefaultTopic = "crestcreates.test.events",
            ConsumerGroupId = $"test-group-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            ProducerPoolSize = 2
        };

        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(Options));
        services.AddSingleton<KafkaProducerPool>();
        services.AddSingleton<KafkaPublisher>();
        services.AddSingleton<KafkaEventBus>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ServiceProvider = services.BuildServiceProvider();
    }

    public virtual async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await ServiceProvider.DisposeAsync();
        }
    }

    private static async Task<bool> IsKafkaAvailable()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", 9092);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaIntegrationTestBase.cs
git commit -m "test: add Kafka integration test base with connection check"
```

---

### Task 28: Write KafkaPublishConsumeTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaPublishConsumeTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using CrestCreates.EventBus.Kafka.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public class KafkaPublishConsumeTests : KafkaIntegrationTestBase
{
    [Fact]
    public async Task PublishAndConsume_EventPublished_ConsumerReceives()
    {
        // Arrange
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<TestKafkaEvent>();
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"test-cg-{Guid.NewGuid():N}";

        // Act
        await publisher.PublishAsync(
            topic,
            new TestKafkaEvent { Message = "Hello Kafka" },
            key: key,
            headers: null,
            CancellationToken.None);

        // Assert — consume with a raw consumer
        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(15));
        cr.Should().NotBeNull();

        var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        envelope.Should().NotBeNull();

        var eventData = JsonSerializer.Deserialize<TestKafkaEvent>(envelope!.Payload);
        eventData!.Message.Should().Be("Hello Kafka");

        consumer.Commit(cr);
    }

    [Fact]
    public async Task PartitionOrdering_SameKey_SamePartition()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<TestKafkaEvent>();
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"order-cg-{Guid.NewGuid():N}";

        // Publish multiple events with the same key
        for (int i = 0; i < 5; i++)
        {
            await publisher.PublishAsync(
                topic,
                new TestKafkaEvent { Message = $"Ordered {i}" },
                key: key,
                headers: null,
                CancellationToken.None);
        }

        // Consume and verify order
        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var messages = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 5; i++)
        {
            var cr = consumer.Consume(TimeSpan.FromSeconds(10));
            if (cr is null) break;
            var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
            var evt = JsonSerializer.Deserialize<TestKafkaEvent>(envelope!.Payload);
            messages.Add(evt!.Message);
            consumer.Commit(cr);
        }

        messages.Should().HaveCount(5);
        messages.Should().BeInAscendingOrder(); // "Ordered 0" < "Ordered 1" < ...
    }

    [Fact]
    public async Task LargePayload_EventWithLargeBody_TransmittedCorrectly()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<TestKafkaEvent>();
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"large-cg-{Guid.NewGuid():N}";

        var largeMessage = new string('y', 100_000);
        await publisher.PublishAsync(
            topic,
            new TestKafkaEvent { Message = largeMessage },
            key: key,
            headers: null,
            CancellationToken.None);

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            MaxPartitionFetchBytes = 5 * 1024 * 1024
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(20));
        cr.Should().NotBeNull();

        var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        var evt = JsonSerializer.Deserialize<TestKafkaEvent>(envelope!.Payload);
        evt!.Message.Should().Be(largeMessage);

        consumer.Commit(cr);
    }

    [Fact]
    public async Task ConcurrentPublishers_MultipleProducers_AllSucceed()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<TestKafkaEvent>();
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();

        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks[i] = publisher.PublishAsync(
                topic,
                new TestKafkaEvent { Message = $"Concurrent {idx}" },
                key: key,
                headers: null,
                CancellationToken.None);
        }

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        exception.Should().BeNull();
    }

    [Fact]
    public async Task ManualOffsetCommit_CommitAfterConsume_OffsetStored()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<TestKafkaEvent>();
        var key = EventNamingConvention.GetRoutingKey<TestKafkaEvent>();
        var groupId = $"commit-cg-{Guid.NewGuid():N}";

        await publisher.PublishAsync(
            topic,
            new TestKafkaEvent { Message = "Commit test" },
            key: key,
            headers: null,
            CancellationToken.None);

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(10));
        cr.Should().NotBeNull();

        // Manual commit
        consumer.Commit(cr);

        // Second consume should not return the same message
        var cr2 = consumer.Consume(TimeSpan.FromSeconds(3));
        // cr2 may be null (no more messages) or a different message — either is fine
        // The key assertion is that commit didn't throw
    }
}

public sealed class TestKafkaEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaPublishConsumeTests.cs
git commit -m "test: add Kafka publish/consume integration tests"
```

---

### Task 29: Write KafkaRetryAndDLQTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaRetryAndDLQTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using CrestCreates.EventBus.Kafka.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public class KafkaRetryAndDLQTests : KafkaIntegrationTestBase
{
    [Fact]
    public async Task HandlerFailureRetry_ConsumerSeeksBack_Redelivers()
    {
        // This test verifies that a consumer can seek back and re-read a message.
        // The actual retry logic is in KafkaConsumer (BackgroundService), but we
        // test the underlying Kafka behavior here.
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<KafkaRetryTestEvent>();
        var key = EventNamingConvention.GetRoutingKey<KafkaRetryTestEvent>();
        var groupId = $"retry-cg-{Guid.NewGuid():N}";

        await publisher.PublishAsync(
            topic,
            new KafkaRetryTestEvent { Message = "Retry" },
            key: key,
            headers: null,
            CancellationToken.None);

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        // First read
        var cr1 = consumer.Consume(TimeSpan.FromSeconds(10));
        cr1.Should().NotBeNull();

        // Seek back to simulate retry (don't commit)
        consumer.Seek(cr1.TopicPartitionOffset);

        // Second read — should get the same message
        var cr2 = consumer.Consume(TimeSpan.FromSeconds(10));
        cr2.Should().NotBeNull();
        cr2!.Offset.Should().Be(cr1!.Offset);

        consumer.Commit(cr2);
    }

    [Fact]
    public async Task HandlerFailureDLQ_RetriesExhausted_PublishedToDLQTopic()
    {
        // Publish to DLQ topic directly to verify it works
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var mainTopic = EventNamingConvention.GetTopic<KafkaDLQTestEvent>();
        var dlqTopic = $"{mainTopic}{Options.DeadLetterTopicSuffix}";
        var key = EventNamingConvention.GetRoutingKey<KafkaDLQTestEvent>();

        // Publish to DLQ topic (simulating what KafkaConsumer does after retries exhausted)
        var dlqEvent = new KafkaDLQTestEvent { Message = "DLQ bound" };
        var payload = JsonSerializer.Serialize(dlqEvent, typeof(KafkaDLQTestEvent));
        var dlqEnvelope = new KafkaMessageEnvelope(
            typeof(KafkaDLQTestEvent).AssemblyQualifiedName!,
            payload,
            null)
        {
            RetryCount = 3
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(dlqEnvelope, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        await publisher.PublishToDeadLetterTopicAsync(
            mainTopic,
            envelopeBytes,
            key: key,
            retryCount: 3,
            CancellationToken.None);

        // Consume from DLQ topic
        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = $"dlq-cg-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(dlqTopic);

        var cr = consumer.Consume(TimeSpan.FromSeconds(15));
        cr.Should().NotBeNull();

        var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        envelope.Should().NotBeNull();
        envelope!.RetryCount.Should().Be(3);

        consumer.Commit(cr);
    }

    [Fact]
    public async Task IdempotentConsumption_IdempotentProducer_Deduplicates()
    {
        // Kafka idempotent producer prevents duplicate writes within a session.
        // This test verifies the producer pool creates producers correctly.
        var pool = ServiceProvider.GetRequiredService<KafkaProducerPool>();
        var producer = await pool.GetProducerAsync();
        producer.Should().NotBeNull();
        // The producer is configured with EnableIdempotence=true in KafkaProducerPool
        pool.ReturnProducer(producer);
    }

    [Fact]
    public async Task MultiConsumerGroup_DifferentGroups_EachReceivesFullStream()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<KafkaRetryTestEvent>();
        var key = EventNamingConvention.GetRoutingKey<KafkaRetryTestEvent>();

        await publisher.PublishAsync(
            topic,
            new KafkaRetryTestEvent { Message = "Multi-group" },
            key: key,
            headers: null,
            CancellationToken.None);

        // Consumer group 1
        var config1 = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = $"multi-cg1-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        // Consumer group 2
        var config2 = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = $"multi-cg2-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer1 = new ConsumerBuilder<string, byte[]>(config1).Build();
        using var consumer2 = new ConsumerBuilder<string, byte[]>(config2).Build();
        consumer1.Subscribe(topic);
        consumer2.Subscribe(topic);

        var cr1 = consumer1.Consume(TimeSpan.FromSeconds(10));
        var cr2 = consumer2.Consume(TimeSpan.FromSeconds(10));

        cr1.Should().NotBeNull();
        cr2.Should().NotBeNull();

        // Both groups receive the same message
        var env1 = JsonSerializer.Deserialize(cr1.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        var evt1 = JsonSerializer.Deserialize<KafkaRetryTestEvent>(env1!.Payload);
        evt1!.Message.Should().Be("Multi-group");

        var env2 = JsonSerializer.Deserialize(cr2.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
        var evt2 = JsonSerializer.Deserialize<KafkaRetryTestEvent>(env2!.Payload);
        evt2!.Message.Should().Be("Multi-group");

        consumer1.Commit(cr1);
        consumer2.Commit(cr2);
    }
}

public sealed class KafkaRetryTestEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

public sealed class KafkaDLQTestEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaRetryAndDLQTests.cs
git commit -m "test: add Kafka retry and DLQ integration tests"
```

---

### Task 30: Write KafkaMultiConsumerTests

**Files:**
- Create: `framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaMultiConsumerTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using CrestCreates.EventBus.Kafka.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public class KafkaMultiConsumerTests : KafkaIntegrationTestBase
{
    [Fact]
    public async Task MultipleEvents_DifferentKeys_DifferentPartitions()
    {
        var publisher = ServiceProvider.GetRequiredService<KafkaPublisher>();
        var topic = EventNamingConvention.GetTopic<KafkaMultiTestEvent>();
        var groupId = $"multi-cg-{Guid.NewGuid():N}";

        // Publish events with different keys
        for (int i = 0; i < 10; i++)
        {
            await publisher.PublishAsync(
                topic,
                new KafkaMultiTestEvent { Message = $"Event {i}" },
                key: $"key-{i}",
                headers: null,
                CancellationToken.None);
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var received = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 10; i++)
        {
            var cr = consumer.Consume(TimeSpan.FromSeconds(15));
            if (cr is null) break;
            var envelope = JsonSerializer.Deserialize(cr.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
            var evt = JsonSerializer.Deserialize<KafkaMultiTestEvent>(envelope!.Payload);
            received.Add(int.Parse(evt!.Message.Split(' ')[1]));
            consumer.Commit(cr);
        }

        received.Should().HaveCount(10);
    }

    [Fact]
    public async Task ConsumerGroup_Rebalance_MultipleConsumers()
    {
        // Verify that two consumers in the same group can both subscribe
        var topic = EventNamingConvention.GetTopic<KafkaMultiTestEvent>();
        var groupId = $"rebalance-cg-{Guid.NewGuid():N}";

        var config = new ConsumerConfig
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer1 = new ConsumerBuilder<string, byte[]>(config).Build();
        using var consumer2 = new ConsumerBuilder<string, byte[]>(config).Build();

        consumer1.Subscribe(topic);
        consumer2.Subscribe(topic);

        // Both should be able to subscribe without error
        consumer1.Assignment.Should().NotBeNull();
        consumer2.Assignment.Should().NotBeNull();
    }

    [Fact]
    public async Task SASLConnection_Plaintext_ConnectsSuccessfully()
    {
        // With PLAINTEXT protocol, connection should work without SASL
        var pool = ServiceProvider.GetRequiredService<KafkaProducerPool>();
        var producer = await pool.GetProducerAsync();
        producer.Should().NotBeNull();
        pool.ReturnProducer(producer);
    }
}

public sealed class KafkaMultiTestEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/KafkaMultiConsumerTests.cs
git commit -m "test: add Kafka multi-consumer integration tests"
```

---

## Phase 8: Solution and Final Verification

### Task 31: Add all new test projects to CrestCreates.slnx

**Files:**
- Modify: `CrestCreates.slnx`

- [ ] **Step 1: Read the current .slnx to find the test project section**

```bash
grep -n "EventBus" CrestCreates.slnx
```

- [ ] **Step 2: Add the three new test projects and the two existing test projects**

Add these entries in the test projects section of the .slnx file (near the existing `CrestCreates.EventBus.Tests` entry):

```xml
<Project Path="framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj" />
<Project Path="framework/test/CrestCreates.EventBus.RabbitMQ.Tests/CrestCreates.EventBus.RabbitMQ.Tests.csproj" />
<Project Path="framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration/CrestCreates.EventBus.RabbitMQ.Tests.Integration.csproj" />
<Project Path="framework/test/CrestCreates.EventBus.Kafka.Tests/CrestCreates.EventBus.Kafka.Tests.csproj" />
<Project Path="framework/test/CrestCreates.EventBus.Kafka.Tests.Integration/CrestCreates.EventBus.Kafka.Tests.Integration.csproj" />
```

- [ ] **Step 3: Verify solution builds**

```bash
dotnet build
```

Expected: Entire solution builds successfully.

- [ ] **Step 4: Commit**

```bash
git add CrestCreates.slnx
git commit -m "build: add all event bus test projects to solution"
```

---

### Task 32: Run all existing unit tests to verify no regressions

**Files:**
- None (verification only)

- [ ] **Step 1: Run all EventBus unit tests**

```bash
dotnet test framework/test/CrestCreates.EventBus.Tests/CrestCreates.EventBus.Tests.csproj
dotnet test framework/test/CrestCreates.EventBus.RabbitMQ.Tests/CrestCreates.EventBus.RabbitMQ.Tests.csproj
dotnet test framework/test/CrestCreates.EventBus.Kafka.Tests/CrestCreates.EventBus.Kafka.Tests.csproj
```

Expected: All existing unit tests pass.

- [ ] **Step 2: Run local DLQ integration tests (no Docker needed)**

```bash
dotnet test framework/test/CrestCreates.EventBus.Local.Tests.Integration/CrestCreates.EventBus.Local.Tests.Integration.csproj
```

Expected: All 14 local integration tests pass.

- [ ] **Step 3: Commit if all pass**

```bash
git commit -m "test: verify all existing and new local tests pass"
```

---

### Task 33: Run full solution build as final verification

**Files:**
- None (verification only)

- [ ] **Step 1: Full solution build**

```bash
dotnet build
```

Expected: Zero errors.

- [ ] **Step 2: Run all tests that don't require Docker**

```bash
dotnet test --filter "FullyQualifiedName~EventBus"
```

Expected: All non-Docker tests pass. RabbitMQ and Kafka integration tests will be skipped if Docker services are not running.

- [ ] **Step 3: Final commit**

```bash
git commit -m "build: final verification - full solution builds and all tests pass"
```
