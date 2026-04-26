# RabbitMQ Distributed Event Bus Implementation Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement AOT-friendly RabbitMQ distributed event bus with connection management, serialization, subscription mechanism, and retry strategy.

**Architecture:** Layered architecture with four layers: Connection (connection pool), Transport (publisher/consumer), Abstraction (event bus), Compile-time (source generator). Uses RabbitMQ.Client with System.Text.Json + SourceGeneratedContext for AOT compatibility.

**Tech Stack:** RabbitMQ.Client, System.Text.Json with JsonSerializerContext, Microsoft.CodeAnalysis (IIncrementalGenerator), BackgroundService

---

## Section 1: Architecture Overview

The RabbitMQ implementation consists of four layers:

1. **Connection Layer** - `RabbitMqConnectionPool` manages a single long-lived connection with automatic recovery, and provides channel pooling for concurrent operations.

2. **Transport Layer** - `RabbitMqPublisher` handles message publishing with publisher confirms; `RabbitMqConsumer` is a `BackgroundService` that consumes messages with retry and DLQ support.

3. **Abstraction Layer** - `RabbitMqEventBus` implements `DistributedEventBusBase`, bridging the framework's event bus interface to the transport layer.

4. **Compile-Time Layer** - `RabbitMqSubscribeAttribute` marks handler methods; `RabbitMqSubscriptionSourceGenerator` in `CrestCreates.CodeGenerator` discovers these at compile-time and generates static registration code.

**Data flow:**
```
Publish: DomainEvent → RabbitMqEventBus → RabbitMqPublisher → RabbitMqConnectionPool → RabbitMQ
Subscribe: RabbitMQ → RabbitMqConsumer → Deserialize → Invoke Handler → Ack/Nack
```

---

## Section 2: Connection Management

**RabbitMqConnectionPool:**
- Maintains a single `IConnection` with automatic reconnection via RabbitMQ.Client's built-in recovery
- Provides `GetChannelAsync()` that returns a channel from an internal `ConcurrentQueue<IModel>`
- Channels are created with publisher confirms enabled for reliable publishing
- Implements `IDisposable` to gracefully close connection and dispose channels
- Thread-safe: uses `SemaphoreSlim` to limit concurrent channel creation (max 10 channels by default)

**Configuration (RabbitMqOptions):**
```csharp
public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public int MaxChannels { get; set; } = 10;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public string DeadLetterExchange { get; set; } = "crestcreates.dlx";
}
```

**Key design decisions:**
- Single connection is standard RabbitMQ pattern (channels handle concurrency)
- Automatic recovery is built into RabbitMQ.Client when `AutomaticRecoveryEnabled = true`
- Channel pooling avoids creating/destroying channels per message

---

## Section 3: Serialization

**RabbitMqMessageSerializer:**
- Uses `System.Text.Json` with `JsonSerializerContext` for AOT-friendly serialization
- Requires users to register event types in a `JsonSerializerContext`-derived class
- Source Generator will validate that event types are registered in the context

**Pattern:**
```csharp
// User defines their event types context
[JsonSerializable(typeof(OrderCreatedEvent))]
[JsonSerializable(typeof(OrderShippedEvent))]
public partial class MyEventsContext : JsonSerializerContext { }

// Register at startup
services.AddRabbitMqEventBus<MyEventsContext>(options => { ... });
```

**Message envelope:**
```csharp
public class RabbitMqMessageEnvelope
{
    public string EventType { get; set; } // Assembly-qualified type name for deserialization
    public string Payload { get; set; }  // JSON-serialized event data
    public Dictionary<string, string?> Headers { get; set; } // CorrelationId, TenantId, etc.
    public DateTime Timestamp { get; set; }
}
```

**Key design decisions:**
- `JsonSerializerContext` is AOT-friendly (no runtime reflection)
- Envelope wraps event with metadata for routing and tracing
- Type name stored as string for deserialization lookup in the generated context

---

## Section 4: Publisher

**RabbitMqPublisher:**
- Injects `RabbitMqConnectionPool` and `JsonSerializerContext`
- `PublishAsync<TEvent>(string exchange, string routingKey, TEvent @event)` method
- Creates channel from pool, declares exchange if needed, publishes with confirmation
- Uses RabbitMQ's publisher confirms for reliable delivery (waits for ack from broker)

**Publishing flow:**
1. Get channel from pool
2. Serialize event to envelope using SourceGenerated context
3. Declare exchange (idempotent operation)
4. Publish with `persistent: true` and `mandatory: true`
5. Wait for confirmation via `WaitForConfirmsAsync()`
6. Return channel to pool

**Exchange naming convention:**
- Default exchange: `crestcreates.events`
- Routing key: Event type name (e.g., `OrderCreatedEvent`)
- User can override via `RabbitMqSubscribeAttribute.Exchange` property

**Error handling:**
- If confirmation times out, throw `RabbitMqPublishException`
- Channel is disposed on error (not returned to pool)
- Connection pool handles reconnection automatically

---

## Section 5: Consumer

**RabbitMqConsumer (BackgroundService):**
- Consumes messages from subscribed queues
- Deserializes envelope, resolves handler, invokes handler, acks/nacks
- Implements retry with fixed delay and dead-letter queue

**Subscription model (generated by Source Generator):**
```csharp
// Generated code
public static class RabbitMqSubscriptionRegistry
{
    public static IReadOnlyList<SubscriptionInfo> GetSubscriptions() => new[]
    {
        new SubscriptionInfo("OrderCreatedEvent", typeof(OrderCreatedHandler), "HandleAsync"),
        new SubscriptionInfo("OrderShippedEvent", typeof(OrderShippedHandler), "HandleAsync"),
    };
}
```

**Consumer flow:**
1. On start, declare queues for each subscription (with DLX binding)
2. Create consumer with `AutoAck = false`
3. On message received:
   - Deserialize envelope
   - Resolve handler from DI scope
   - Invoke handler method
   - On success: `channel.BasicAck`
   - On failure: increment retry count in header, requeue or send to DLX

**Retry mechanism:**
- Retry count stored in message headers (`x-retry-count`)
- If retry count < max: requeue with delay (using `x-delay` header or delayed exchange plugin)
- If retry count >= max: reject and route to DLX

**Queue naming:**
- Default: `crestcreates.{EventType}`
- DLQ: `crestcreates.{EventType}.dlq`

---

## Section 6: Source Generator

**RabbitMqSubscriptionSourceGenerator (in CrestCreates.CodeGenerator):**

Scans for methods marked with `[RabbitMqSubscribe]` attribute and generates static registration code.

**Attribute definition:**
```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RabbitMqSubscribeAttribute : Attribute
{
    public string EventType { get; } // Event type name to subscribe to
    public string? Exchange { get; set; } // Optional: override exchange
    public string? Queue { get; set; } // Optional: override queue name
    public int PrefetchCount { get; set; } = 10; // Consumer prefetch

    public RabbitMqSubscribeAttribute(string eventType)
    {
        EventType = eventType;
    }
}
```

**Handler pattern (user code):**
```csharp
public class OrderEventHandler
{
    [RabbitMqSubscribe("OrderCreatedEvent")]
    public async Task HandleOrderCreatedAsync(OrderCreatedEvent @event, CancellationToken ct)
    {
        // Handle event
    }
}
```

**Generated output (RabbitMqSubscriptionRegistry.g.cs):**
```csharp
namespace CrestCreates.EventBus.RabbitMQ.Generated
{
    public static class RabbitMqSubscriptionRegistry
    {
        public static IReadOnlyList<RabbitMqSubscriptionInfo> GetSubscriptions() => _subscriptions;

        private static readonly IReadOnlyList<RabbitMqSubscriptionInfo> _subscriptions = new[]
        {
            new RabbitMqSubscriptionInfo(
                EventType: "OrderCreatedEvent",
                HandlerType: typeof(OrderEventHandler),
                HandlerMethod: "HandleOrderCreatedAsync",
                Exchange: "crestcreates.events",
                Queue: "crestcreates.OrderCreatedEvent",
                PrefetchCount: 10),
        };
    }
}
```

**Generator logic:**
1. Find all methods with `[RabbitMqSubscribe]` attribute
2. Extract event type, handler type, method name, and optional settings
3. Generate `RabbitMqSubscriptionInfo` for each
4. Output static registry class

---

## Section 7: Service Registration and Integration

**RabbitMqEventBus:**
- Implements `DistributedEventBusBase`
- Delegates `PublishAsync` to `RabbitMqPublisher`
- `Subscribe`/`Unsubscribe` throw `NotSupportedException` (subscriptions are compile-time generated)

**Extension method:**
```csharp
public static class RabbitMqEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqEventBus<TContext>(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null)
        where TContext : JsonSerializerContext
    {
        // Register options
        services.Configure<RabbitMqOptions>(configure ?? (_ => { }));

        // Register JsonSerializerContext as singleton
        services.AddSingleton<JsonSerializerContext>(sp =>
            Activator.CreateInstance<TContext>()!);

        // Register connection pool as singleton
        services.AddSingleton<RabbitMqConnectionPool>();

        // Register publisher as transient
        services.AddTransient<RabbitMqPublisher>();

        // Register event bus
        services.AddSingleton<IEventBus, RabbitMqEventBus>();

        // Register consumer as hosted service
        services.AddHostedService<RabbitMqConsumer>();

        return services;
    }
}
```

**Module integration:**
```csharp
public class RabbitMqEventBusModule : ModuleBase
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Module provides defaults, user overrides in startup
        context.Services.AddRabbitMqEventBus<AppJsonSerializerContext>(options =>
        {
            options.HostName = "localhost";
        });
    }
}
```

---

## Section 8: File Structure

**New files in `CrestCreates.EventBus.RabbitMQ`:**
- `Options/RabbitMqOptions.cs` - Configuration
- `Options/RabbitMqSubscriptionInfo.cs` - Subscription metadata
- `Connection/RabbitMqConnectionPool.cs` - Connection/channel management
- `Serialization/RabbitMqMessageEnvelope.cs` - Message wrapper
- `Serialization/RabbitMqMessageSerializer.cs` - JSON serialization helper
- `Publishing/RabbitMqPublisher.cs` - Message publisher
- `Consuming/RabbitMqConsumer.cs` - Background service consumer
- `Attributes/RabbitMqSubscribeAttribute.cs` - Subscription marker
- `RabbitMqEventBus.cs` - IEventBus implementation
- `Extensions/RabbitMqEventBusServiceCollectionExtensions.cs` - DI registration
- `RabbitMqEventBusModule.cs` - Module definition

**New files in `CrestCreates.CodeGenerator`:**
- `RabbitMqGenerator/RabbitMqSubscriptionSourceGenerator.cs` - Source generator
- `RabbitMqGenerator/RabbitMqSubscriptionModel.cs` - Generator model
- `RabbitMqGenerator/RabbitMqSubscriptionCodeWriter.cs` - Code output

**Updated csproj:**
- Add `RabbitMQ.Client` package reference
- Add `System.Text.Json` package reference

---

## Section 9: Error Handling and Testing

**Error handling:**
- `RabbitMqConnectionException` - Connection failures
- `RabbitMqPublishException` - Publishing failures (confirmation timeout)
- `RabbitMqConsumeException` - Deserialization or handler invocation failures
- All exceptions include correlation ID and event type for tracing

**Logging:**
- Connection events (connected, disconnected, reconnecting)
- Publish events (published, confirmed, failed)
- Consume events (received, handled, retrying, dead-lettered)
- Use `ILogger` with structured logging

**Testing approach:**
- Unit tests: Mock `IModel` and `IConnection` for publisher/consumer logic
- Integration tests: Use Testcontainers for RabbitMQ (Docker-based)
- Test categories:
  - Connection recovery
  - Publish with confirmation
  - Consume with retry
  - DLQ routing
  - Serialization round-trip

**Test files:**
- `tests/CrestCreates.EventBus.RabbitMQ.Tests/ConnectionPoolTests.cs`
- `tests/CrestCreates.EventBus.RabbitMQ.Tests/PublisherTests.cs`
- `tests/CrestCreates.EventBus.RabbitMQ.Tests/ConsumerTests.cs`
- `tests/CrestCreates.EventBus.RabbitMQ.Tests/IntegrationTests.cs` (Testcontainers)

---

## Acceptance Criteria

1. RabbitMQ connection pool maintains single connection with automatic recovery
2. Publisher confirms enabled for reliable message delivery
3. Consumer implements fixed-delay retry with DLQ fallback
4. All serialization uses SourceGenerated JsonSerializerContext (AOT-friendly)
5. Source Generator generates static subscription registry from `[RabbitMqSubscribe]` attributes
6. RabbitMqEventBus implements `DistributedEventBusBase` correctly
7. Module integrates with framework's modular architecture
8. Unit tests cover core functionality with mocked RabbitMQ
9. Integration tests use Testcontainers for real RabbitMQ testing
10. All builds pass with 0 errors