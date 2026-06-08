# Event Bus Stability Design

**Date**: 2026-06-08
**Status**: Design Approved

## Overview

补全事件总线稳定性：本地事件 DLQ、命名规范、集成测试。

Current state:
- RabbitMQ DLQ: ✅ implemented (DLX + per-queue DLQ)
- Kafka DLQ: ✅ implemented (.dlq suffix topic)
- Idempotency store: ⚠️ interface exists, NOT wired into consumers
- Naming convention: ❌ none defined
- Integration tests: ❌ none (all unit tests only)
- Docker/compose: ❌ none
- Local DLQ: ❌ not implemented

## Layer 1: Local Dead Letter Queue

### New Files in Abstractions (`CrestCreates.EventBus.Abstractions`)

| File | Content |
|------|---------|
| `ILocalDeadLetterStore.cs` | `EnqueueAsync`, `GetAsync`, `ListAsync`, `MarkRetryingAsync`, `MarkRetriedAsync`, `RemoveAsync`, `CountAsync` |
| `ILocalDeadLetterManager.cs` | `ListAsync`, `RetryAsync`, `RetryAllAsync`, `ClearAsync`, `GetStatsAsync` |
| `DeadLetterMessage.cs` | Record: MessageId, EventType, Payload(byte[]), Error, FailedAt, RetryCount, MaxRetries, Status |
| `DeadLetterStatus.cs` | Enum: Pending, Retrying, Retried, Archived |
| `LocalDeadLetterOptions.cs` | MaxRetries(3), RetryIntervalSeconds(30), MaxQueueSize(10000), AutoCleanArchivedDays(7) |

### New Files in `CrestCreates.EventBus.Local.Channel`

| File | Content |
|------|---------|
| `InMemoryDeadLetterStore.cs` | ConcurrentDictionary storage, thread-safe |
| `LocalDeadLetterBackgroundService.cs` | BackgroundService, periodic scan + retry |
| `DefaultLocalDeadLetterManager.cs` | Management API: paged list, single retry, batch retry, clear |

### Modified Files

| File | Change |
|------|--------|
| `DefaultLocalEventBus.cs` | Catch exception → write to ILocalDeadLetterStore |
| `BackgroundChannelLocalEventBusConsumer.cs` | Same as above |
| `LocalEventBusModule.cs` | Register DLQ services |
| `LocalChannelEventBusModule.cs` | Register DLQ services (with BackgroundService) |

### Dead Letter Flow

```
Event handler fails
  → catch Exception
  → DeadLetterMessage (Status=Pending)
  → ILocalDeadLetterStore.EnqueueAsync()

LocalDeadLetterBackgroundService (every 30s scan)
  → Find Status=Pending, RetryCount < MaxRetries
  → MarkRetryingAsync()
  → Re-dispatch event
  → Success: MarkRetriedAsync() → Status=Retried
  → Failure: RetryCount++, Status=Pending (wait next round)
  → RetryCount >= MaxRetries → Status=Archived (stop retrying)

User via ILocalDeadLetterManager:
  → RetryAsync(messageId) manually retry single
  → RetryAllAsync() retry all Pending/Archived
  → ClearAsync(eventType?) clear Retried/Archived
  → ListAsync() paged view
  → GetStatsAsync() summary statistics
```

### Scope Boundary

Local DLQ only serves `ILocalEventBus` (in-process events). RabbitMQ/Kafka DLQ handled by their own consumer retry logic (existing), separate from this path.

## Layer 2: Naming Convention

### Naming Patterns

| Concern | Pattern | Example |
|---------|---------|---------|
| Event class | `{Aggregate}{Action}Event` (PascalCase) | `ProductCreatedEvent`, `OrderPaidEvent` |
| Integration event | `{Aggregate}{Action}IntegrationEvent` | `ProductCreatedIntegrationEvent` |
| Routing key (RabbitMQ) | `{bounded-context}.{aggregate}.{action}` (lower_underscore) | `order.order.placed`, `product.product.created` |
| Topic (Kafka) | `{bounded-context}.events` | `order.events`, `product.events` |
| Exchange (RabbitMQ) | `crestcreates.{bounded-context}.events` | `crestcreates.order.events` |
| Queue (RabbitMQ) | `{service-name}.{bounded-context}.{aggregate}.{action}` | `notification-service.order.order.placed` |
| Consumer Group (Kafka) | `{service-name}.{bounded-context}` | `notification-service.order` |
| DLQ (RabbitMQ) | `{queue}.dlq` | `notification-service.order.order.placed.dlq` |
| DLQ (Kafka) | `{topic}.dlq` | `order.events.dlq` |

### Static Helper Class

`EventNamingConvention` in `CrestCreates.EventBus.Abstract`:

```csharp
public static class EventNamingConvention
{
    public static string GetRoutingKey<TEvent>() where TEvent : class;
    public static string GetRoutingKey(string boundedContext, string aggregate, string action);
    public static string GetTopic<TEvent>();
    public static string GetTopic(string boundedContext);
    public static string GetExchange(string boundedContext);
    public static string GetQueue(string serviceName, string routingKey);
    public static string GetConsumerGroup(string serviceName, string boundedContext);
    public static string GetDeadLetterQueue(string queue);
    public static string GetDeadLetterTopic(string topic);
}
```

### Modified Files

| File | Change |
|------|--------|
| `RabbitMqEventBus.cs` | Use `EventNamingConvention.GetRoutingKey()` instead of `typeof(TEvent).Name` |
| `KafkaEventBus.cs` | Use `EventNamingConvention.GetTopic()` instead of `typeof(TEvent).FullName` |
| `RabbitMqConsumer.cs` | Queue naming via `EventNamingConvention.GetQueue()` |
| `KafkaConsumer.cs` | Consumer Group via `EventNamingConvention.GetConsumerGroup()` |
| `RabbitMqOptions.cs` | Exchange naming via `EventNamingConvention.GetExchange()` |
| Existing tests | Update assertions to match new naming rules |

### Service Name Convention

Service name must be **lower-kebab-case** (e.g., `notification-service`, `order-api`). It is injected as a string option (`EventBusOptions.ServiceName`), defaulting to the entry assembly name converted to lower-kebab-case. Consumers pass it explicitly to `GetQueue()` and `GetConsumerGroup()`.

## Layer 3: Integration Tests

### Docker Compose

**File**: `infra/docker-compose.eventbus.yml`

```yaml
services:
  rabbitmq:
    image: rabbitmq:4.0-management-alpine
    ports:
      - "5672:5672"
      - "15672:15672"
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_port_connectivity"]
      interval: 10s
      timeout: 5s
      retries: 10

  kafka:
    image: apache/kafka:4.0.0
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
```

### Test Project Structure

```
framework/test/
├── CrestCreates.EventBus.Local.Tests.Integration/       # NEW
│   ├── LocalDeadLetterQueueTests.cs
│   ├── LocalEventBusDispatchTests.cs
│   ├── LocalEventBusIdempotencyTests.cs
│   └── *.csproj
│
├── CrestCreates.EventBus.RabbitMQ.Tests.Integration/    # NEW
│   ├── RabbitMqIntegrationTestBase.cs
│   ├── RabbitMqPublishConsumeTests.cs
│   ├── RabbitMqRetryAndDLQTests.cs
│   ├── RabbitMqMultiHandlerTests.cs
│   └── *.csproj
│
├── CrestCreates.EventBus.Kafka.Tests.Integration/       # NEW
│   ├── KafkaIntegrationTestBase.cs
│   ├── KafkaPublishConsumeTests.cs
│   ├── KafkaRetryAndDLQTests.cs
│   ├── KafkaMultiConsumerTests.cs
│   └── *.csproj
```

### Test Cases

**Local DLQ** (12 cases):

| Test | Scenario |
|------|----------|
| EnqueueFailingEvent | Handler throws → event enters DLQ |
| RetrySucceeds | DLQ retry → handler succeeds → marked Retried |
| RetryExhausted | Retries reach MaxRetries → Status=Archived |
| ManualRetry | ILocalDeadLetterManager.RetryAsync manual retry |
| RetryAll | RetryAllAsync batch retry all |
| ClearByEventType | ClearAsync("ProductCreatedEvent") clears specified type |
| ListWithFilter | ListAsync with paging/type filter |
| IdempotencyWithDLQ | Duplicate event → idempotency check passes → no duplicate processing |
| ConcurrentEnqueue | Multi-threaded concurrent enqueue → data consistency |
| BackgroundRetryTimer | BackgroundService periodic scan + retry |
| DeadLetterStats | GetStatsAsync returns summary statistics |
| MaxQueueSizeProtection | Overflow capacity protection |

**RabbitMQ** (10 cases):

| Test | Scenario |
|------|----------|
| PublishAndConsume | Publish event → consumer receives and processes |
| MultiHandlerDispatch | Multiple queues bound to same exchange → each receives |
| HandlerFailureRetry | Handler fails → nack → retry → success |
| HandlerFailureDLQ | Retries exhausted → enters DLQ |
| IdempotentConsumption | Same MessageId redelivered → idempotency skip |
| ConnectionRecovery | Auto-recovery after disconnect |
| LargePayload | Large body event transmitted correctly |
| PublisherConfirmation | Publisher confirm acknowledgement |
| UnknownEventType | Unknown type → DLQ |
| ConcurrentPublishers | Multiple publishers concurrently |

**Kafka** (10 cases):

| Test | Scenario |
|------|----------|
| PublishAndConsume | Publish → consumer receives |
| PartitionOrdering | Same key → same partition → ordered |
| HandlerFailureRetry | Failure → seek retry → success |
| HandlerFailureDLQ | Retries exhausted → .dlq topic |
| IdempotentConsumption | Idempotent producer + consumer dedup |
| MultiConsumerGroup | Different groups each consume full stream |
| ManualOffsetCommit | Commit semantics correct |
| LargePayload | Large body event OK |
| SASLConnection | SASL auth (optional) |
| ConcurrentPublishers | Multiple producers concurrently |

### Run Instructions

```bash
# Start dependencies
docker compose -f infra/docker-compose.eventbus.yml up -d

# Run integration tests
dotnet test framework/test/CrestCreates.EventBus.Local.Tests.Integration
dotnet test framework/test/CrestCreates.EventBus.RabbitMQ.Tests.Integration
dotnet test framework/test/CrestCreates.EventBus.Kafka.Tests.Integration

# Local DLQ tests do NOT require Docker

# Cleanup
docker compose -f infra/docker-compose.eventbus.yml down -v
```

## Acceptance Criteria

1. Local event handler failure → event stored in DLQ with error details
2. Background service retries DLQ events at configured interval
3. After MaxRetries exhausted, event archived (no more auto-retry)
4. ILocalDeadLetterManager supports: List (paged/filtered), Retry (single), RetryAll, Clear, GetStats
5. EventNamingConvention static class with all helper methods listed above
6. RabbitMQ routing keys use `{bounded-context}.{aggregate}.{action}` format
7. Kafka topics use `{bounded-context}.events` format
8. Existing unit tests pass with updated naming conventions
9. 32 integration tests (12 local + 10 RabbitMQ + 10 Kafka) all pass
10. docker-compose.yml healthchecks ensure RabbitMQ and Kafka are ready before tests
11. Integration test projects added to CrestCreates.slnx