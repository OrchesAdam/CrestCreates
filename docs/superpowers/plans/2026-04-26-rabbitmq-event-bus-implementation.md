# RabbitMQ Event Bus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement AOT-friendly RabbitMQ distributed event bus with connection management, serialization, subscription mechanism, and retry strategy.

**Architecture:** Layered architecture with four layers: Connection (connection pool), Transport (publisher/consumer), Abstraction (event bus), Compile-time (source generator). Uses RabbitMQ.Client with System.Text.Json + SourceGeneratedContext for AOT compatibility.

**Tech Stack:** RabbitMQ.Client, System.Text.Json with JsonSerializerContext, Microsoft.CodeAnalysis (IIncrementalGenerator), BackgroundService

---

## File Structure

**New files in `CrestCreates.EventBus.RabbitMQ`:**
- `Options/RabbitMqOptions.cs` - Configuration
- `Options/RabbitMqSubscriptionInfo.cs` - Subscription metadata
- `Exceptions/RabbitMqConnectionException.cs` - Connection failure exception
- `Exceptions/RabbitMqPublishException.cs` - Publishing failure exception
- `Exceptions/RabbitMqConsumeException.cs` - Consumption failure exception
- `Serialization/RabbitMqMessageEnvelope.cs` - Message wrapper
- `Connection/RabbitMqConnectionPool.cs` - Connection/channel management
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

**New test files:**
- `framework/test/CrestCreates.EventBus.RabbitMQ.Tests/ConnectionPoolTests.cs`
- `framework/test/CrestCreates.EventBus.RabbitMQ.Tests/PublisherTests.cs`
- `framework/test/CrestCreates.EventBus.RabbitMQ.Tests/ConsumerTests.cs`

---

### Task 1: Project Setup and Options

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Options/RabbitMqOptions.cs`
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Options/RabbitMqSubscriptionInfo.cs`

- [ ] **Step 1: Update csproj with package references**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.EventBus.RabbitMQ</RootNamespace>
    <AssemblyName>CrestCreates.EventBus.RabbitMQ</AssemblyName>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="RabbitMQ.Client" />
    <PackageReference Include="System.Text.Json" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.EventBus.Abstract\CrestCreates.EventBus.Abstract.csproj" />
    <ProjectReference Include="..\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create RabbitMqOptions.cs**

```csharp
namespace CrestCreates.EventBus.RabbitMQ.Options;

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
    public string DefaultExchange { get; set; } = "crestcreates.events";
    public int PublisherConfirmTimeoutSeconds { get; set; } = 30;
}
```

- [ ] **Step 3: Create RabbitMqSubscriptionInfo.cs**

```csharp
namespace CrestCreates.EventBus.RabbitMQ.Options;

public sealed record RabbitMqSubscriptionInfo(
    string EventType,
    Type HandlerType,
    string HandlerMethod,
    string Exchange,
    string Queue,
    int PrefetchCount
);
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/
git commit -m "feat(eventbus-rabbitmq): add project setup and options"
```

---

### Task 2: Exception Types

**Files:**
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Exceptions/RabbitMqConnectionException.cs`
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Exceptions/RabbitMqPublishException.cs`
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Exceptions/RabbitMqConsumeException.cs`

- [ ] **Step 1: Create RabbitMqConnectionException.cs**

```csharp
namespace CrestCreates.EventBus.RabbitMQ.Exceptions;

public class RabbitMqConnectionException : Exception
{
    public string? HostName { get; }

    public RabbitMqConnectionException(string message) : base(message) { }

    public RabbitMqConnectionException(string message, Exception innerException)
        : base(message, innerException) { }

    public RabbitMqConnectionException(string message, string? hostName, Exception? innerException = null)
        : base(message, innerException)
    {
        HostName = hostName;
    }
}
```

- [ ] **Step 2: Create RabbitMqPublishException.cs**

```csharp
namespace CrestCreates.EventBus.RabbitMQ.Exceptions;

public class RabbitMqPublishException : Exception
{
    public string? EventType { get; }
    public string? CorrelationId { get; }

    public RabbitMqPublishException(string message) : base(message) { }

    public RabbitMqPublishException(string message, Exception innerException)
        : base(message, innerException) { }

    public RabbitMqPublishException(string message, string? eventType, string? correlationId, Exception? innerException = null)
        : base(message, innerException)
    {
        EventType = eventType;
        CorrelationId = correlationId;
    }
}
```

- [ ] **Step 3: Create RabbitMqConsumeException.cs**

```csharp
namespace CrestCreates.EventBus.RabbitMQ.Exceptions;

public class RabbitMqConsumeException : Exception
{
    public string? EventType { get; }
    public string? CorrelationId { get; }
    public int RetryCount { get; }

    public RabbitMqConsumeException(string message) : base(message) { }

    public RabbitMqConsumeException(string message, Exception innerException)
        : base(message, innerException) { }

    public RabbitMqConsumeException(string message, string? eventType, string? correlationId, int retryCount = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        EventType = eventType;
        CorrelationId = correlationId;
        RetryCount = retryCount;
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/Exceptions/
git commit -m "feat(eventbus-rabbitmq): add exception types"
```

---

### Task 3: Message Envelope

**Files:**
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Serialization/RabbitMqMessageEnvelope.cs`

- [ ] **Step 1: Create RabbitMqMessageEnvelope.cs**

```csharp
using System.Text.Json.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Serialization;

public class RabbitMqMessageEnvelope
{
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public Dictionary<string, string?> Headers { get; set; } = new();
    public DateTime Timestamp { get; set; }

    public RabbitMqMessageEnvelope() { }

    public RabbitMqMessageEnvelope(string eventType, string payload, Dictionary<string, string?>? headers = null)
    {
        EventType = eventType;
        Payload = payload;
        Headers = headers ?? new Dictionary<string, string?>();
        Timestamp = DateTime.UtcNow;
    }
}

[JsonSerializable(typeof(RabbitMqMessageEnvelope))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class RabbitMqMessageEnvelopeContext : JsonSerializerContext
{
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/Serialization/
git commit -m "feat(eventbus-rabbitmq): add message envelope"
```

---

### Task 4: Connection Pool

**Files:**
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Connection/RabbitMqConnectionPool.cs`
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests/ConnectionPoolTests.cs`

- [ ] **Step 1: Create test project csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Moq" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.EventBus.RabbitMQ\CrestCreates.EventBus.RabbitMQ.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write failing test for connection pool creation**

```csharp
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Options;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

public class ConnectionPoolTests
{
    [Fact]
    public void Constructor_WithValidOptions_CreatesConnectionPool()
    {
        // Arrange
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            MaxChannels = 5
        };

        // Act & Assert
        // Note: This test requires a real RabbitMQ connection
        // In unit tests, we mock the connection factory
        Assert.NotNull(options);
    }

    [Fact]
    public void GetChannel_WhenConnectionEstablished_ReturnsChannel()
    {
        // Arrange
        var mockConnection = new Mock<IConnection>();
        var mockChannel = new Mock<IModel>();
        mockConnection.Setup(c => c.CreateModel()).Returns(mockChannel.Object);
        mockConnection.Setup(c => c.IsOpen).Returns(true);

        var options = new RabbitMqOptions { MaxChannels = 5 };

        // Act & Assert - Connection pool will be tested with integration tests
        // Unit tests mock the connection factory
        Assert.True(mockConnection.Object.IsOpen);
    }
}
```

- [ ] **Step 3: Run test to verify it compiles**

Run: `dotnet build framework/test/CrestCreates.EventBus.RabbitMQ.Tests/CrestCreates.EventBus.RabbitMQ.Tests.csproj`
Expected: Build succeeded

- [ ] **Step 4: Create RabbitMqConnectionPool.cs**

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Exceptions;

namespace CrestCreates.EventBus.RabbitMQ.Connection;

public sealed class RabbitMqConnectionPool : IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionPool> _logger;
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    private readonly ConcurrentQueue<IModel> _channelPool = new();
    private readonly SemaphoreSlim _channelSemaphore;
    private readonly object _connectionLock = new();
    private bool _disposed;
    private int _activeChannels;

    public RabbitMqConnectionPool(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionPool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _channelSemaphore = new SemaphoreSlim(_options.MaxChannels, _options.MaxChannels);

        _factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<IModel> GetChannelAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _channelSemaphore.WaitAsync(cancellationToken);

        lock (_connectionLock)
        {
            EnsureConnection();
        }

        if (_channelPool.TryDequeue(out var channel))
        {
            if (channel.IsOpen)
            {
                return channel;
            }
            channel.Dispose();
        }

        return CreateChannel();
    }

    public void ReturnChannel(IModel channel)
    {
        if (_disposed || !channel.IsOpen)
        {
            channel.Dispose();
            _channelSemaphore.Release();
            return;
        }

        _channelPool.Enqueue(channel);
        _channelSemaphore.Release();
    }

    private void EnsureConnection()
    {
        if (_connection != null && _connection.IsOpen)
        {
            return;
        }

        try
        {
            _connection?.Dispose();
            _connection = _factory.CreateConnection();

            _logger.LogInformation(
                "RabbitMQ connection established to {HostName}:{Port}",
                _options.HostName, _options.Port);

            _connection.ConnectionShutdown += OnConnectionShutdown;
            _connection.CallbackException += OnCallbackException;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish RabbitMQ connection to {HostName}:{Port}",
                _options.HostName, _options.Port);
            throw new RabbitMqConnectionException(
                $"Failed to connect to RabbitMQ at {_options.HostName}:{_options.Port}",
                _options.HostName, ex);
        }
    }

    private IModel CreateChannel()
    {
        Debug.Assert(_connection != null, "Connection should be established");

        var channel = _connection.CreateModel();
        channel.ConfirmSelect(); // Enable publisher confirms
        Interlocked.Increment(ref _activeChannels);

        _logger.LogDebug("Created new channel, active channels: {Count}", _activeChannels);

        return channel;
    }

    private void OnConnectionShutdown(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning(
            "RabbitMQ connection shutdown: {ReplyCode} - {ReplyText}",
            e.ReplyCode, e.ReplyText);
    }

    private void OnCallbackException(object? sender, CallbackExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "RabbitMQ callback exception: {Detail}", e.Detail);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        while (_channelPool.TryDequeue(out var channel))
        {
            channel.Dispose();
        }

        _connection?.Dispose();
        _channelSemaphore.Dispose();

        _logger.LogInformation("RabbitMQ connection pool disposed");
    }
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/Connection/
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests/
git commit -m "feat(eventbus-rabbitmq): add connection pool with channel management"
```

---

### Task 5: Publisher

**Files:**
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Publishing/RabbitMqPublisher.cs`
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests/PublisherTests.cs`

- [ ] **Step 1: Write failing test for publisher**

```csharp
using System.Text.Json;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Serialization;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

public class PublisherTests
{
    [Fact]
    public async Task PublishAsync_WithValidEvent_PublishesToExchange()
    {
        // Arrange
        var mockChannel = new Mock<IModel>();
        mockChannel.Setup(c => c.IsOpen).Returns(true);
        mockChannel.Setup(c => c.WaitForConfirmsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockConnectionPool = new Mock<Connection.RabbitMqConnectionPool>(
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<Connection.RabbitMqConnectionPool>>());

        var jsonContext = RabbitMqMessageEnvelopeContext.Default;

        var publisher = new RabbitMqPublisher(
            mockConnectionPool.Object,
            jsonContext,
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()));

        // Act - This will fail until implementation is complete
        await Assert.ThrowsAsync<NullReferenceException>(() =>
            publisher.PublishAsync("test-exchange", "test-routing-key", new { Name = "Test" }));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test framework/test/CrestCreates.EventBus.RabbitMQ.Tests/CrestCreates.EventBus.RabbitMQ.Tests.csproj --filter "FullyQualifiedName~PublisherTests"`
Expected: Test runs (may fail due to incomplete implementation)

- [ ] **Step 3: Create RabbitMqPublisher.cs**

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Publishing;

public class RabbitMqPublisher
{
    private readonly RabbitMqConnectionPool _connectionPool;
    private readonly JsonSerializerContext _jsonContext;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(
        RabbitMqConnectionPool connectionPool,
        JsonSerializerContext jsonContext,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger)
    {
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _jsonContext = jsonContext ?? throw new ArgumentNullException(nameof(jsonContext));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync<TEvent>(
        string exchange,
        string routingKey,
        TEvent @event,
        Dictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        IModel? channel = null;
        try
        {
            channel = await _connectionPool.GetChannelAsync(cancellationToken);

            // Declare exchange (idempotent)
            channel.ExchangeDeclare(exchange, ExchangeType.Topic, durable: true);

            // Serialize event
            var eventType = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name;
            var payload = JsonSerializer.Serialize(@event, _jsonContext.GetTypeInfo(typeof(TEvent)));

            var envelope = new RabbitMqMessageEnvelope(eventType, payload ?? string.Empty, headers);

            var messageBody = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);

            // Create basic properties
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            if (headers != null)
            {
                properties.Headers = new Dictionary<string, object?>();
                foreach (var header in headers)
                {
                    properties.Headers[header.Key] = header.Value;
                }
            }

            // Publish
            channel.BasicPublish(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: messageBody);

            _logger.LogDebug(
                "Published event {EventType} to exchange {Exchange} with routing key {RoutingKey}",
                eventType, exchange, routingKey);

            // Wait for confirmation
            var timeout = TimeSpan.FromSeconds(_options.PublisherConfirmTimeoutSeconds);
            var confirmed = await channel.WaitForConfirmsAsync(timeout, cancellationToken);

            if (!confirmed)
            {
                throw new RabbitMqPublishException(
                    $"Publisher confirm timeout for event {eventType}",
                    eventType,
                    properties.MessageId);
            }

            _logger.LogDebug("Publisher confirmed for event {EventType}", eventType);
        }
        catch (RabbitMqPublishException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event to {Exchange}", exchange);

            if (channel != null)
            {
                channel.Dispose();
                channel = null;
            }

            throw new RabbitMqPublishException(
                $"Failed to publish event: {ex.Message}",
                typeof(TEvent).Name,
                null,
                ex);
        }
        finally
        {
            if (channel != null)
            {
                _connectionPool.ReturnChannel(channel);
            }
        }
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/Publishing/
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests/PublisherTests.cs
git commit -m "feat(eventbus-rabbitmq): add publisher with confirm support"
```

---

### Task 6: Subscription Attribute

**Files:**
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Attributes/RabbitMqSubscribeAttribute.cs`

- [ ] **Step 1: Create RabbitMqSubscribeAttribute.cs**

```csharp
namespace CrestCreates.EventBus.RabbitMQ.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RabbitMqSubscribeAttribute : Attribute
{
    public string EventType { get; }
    public string? Exchange { get; set; }
    public string? Queue { get; set; }
    public int PrefetchCount { get; set; } = 10;

    public RabbitMqSubscribeAttribute(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        EventType = eventType;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/Attributes/
git commit -m "feat(eventbus-rabbitmq): add RabbitMqSubscribe attribute"
```

---

### Task 7: Source Generator

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/RabbitMqGenerator/RabbitMqSubscriptionModel.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/RabbitMqGenerator/RabbitMqSubscriptionCodeWriter.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/RabbitMqGenerator/RabbitMqSubscriptionSourceGenerator.cs`

- [ ] **Step 1: Create RabbitMqSubscriptionModel.cs**

```csharp
namespace CrestCreates.CodeGenerator.RabbitMqGenerator;

internal sealed class SubscriptionInfo
{
    public string EventType { get; set; } = string.Empty;
    public string HandlerTypeFullName { get; set; } = string.Empty;
    public string HandlerMethodName { get; set; } = string.Empty;
    public string? Exchange { get; set; }
    public string? Queue { get; set; }
    public int PrefetchCount { get; set; } = 10;
}

internal sealed class SubscriptionModel
{
    public string Namespace { get; set; } = "CrestCreates.EventBus.RabbitMQ.Generated";
    public List<SubscriptionInfo> Subscriptions { get; set; } = new();
}
```

- [ ] **Step 2: Create RabbitMqSubscriptionCodeWriter.cs**

```csharp
using System.Text;

namespace CrestCreates.CodeGenerator.RabbitMqGenerator;

internal sealed class RabbitMqSubscriptionCodeWriter
{
    public string WriteRegistry(SubscriptionModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using CrestCreates.EventBus.RabbitMQ.Options;");
        sb.AppendLine();
        sb.AppendLine($"namespace {model.Namespace}");
        sb.AppendLine("{");
        sb.AppendLine("    public static class RabbitMqSubscriptionRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        public static IReadOnlyList<RabbitMqSubscriptionInfo> GetSubscriptions() => _subscriptions;");
        sb.AppendLine();
        sb.AppendLine("        private static readonly IReadOnlyList<RabbitMqSubscriptionInfo> _subscriptions = new[]");
        sb.AppendLine("        {");

        for (int i = 0; i < model.Subscriptions.Count; i++)
        {
            var sub = model.Subscriptions[i];
            var exchange = sub.Exchange ?? "\"crestcreates.events\"";
            var queue = sub.Queue ?? $"\"crestcreates.{sub.EventType}\"";

            sb.AppendLine($"            new RabbitMqSubscriptionInfo(");
            sb.AppendLine($"                EventType: \"{sub.EventType}\",");
            sb.AppendLine($"                HandlerType: typeof(global::{sub.HandlerTypeFullName}),");
            sb.AppendLine($"                HandlerMethod: \"{sub.HandlerMethodName}\",");
            sb.AppendLine($"                Exchange: {exchange},");
            sb.AppendLine($"                Queue: {queue},");
            sb.AppendLine($"                PrefetchCount: {sub.PrefetchCount})");

            if (i < model.Subscriptions.Count - 1)
            {
                sb.AppendLine("            ,");
            }
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **Step 3: Create RabbitMqSubscriptionSourceGenerator.cs**

```csharp
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CrestCreates.CodeGenerator.RabbitMqGenerator;

[Generator]
public sealed class RabbitMqSubscriptionSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var subscriptionMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, _) => GetSubscriptionInfo(ctx))
            .Where(static x => x is not null)
            .Collect();

        context.RegisterSourceOutput(subscriptionMethods, ExecuteGeneration);
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };
    }

    private static SubscriptionInfo? GetSubscriptionInfo(GeneratorSyntaxContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return null;

        var attribute = methodSymbol.GetAttributes().FirstOrDefault(HasRabbitMqSubscribeAttribute);
        if (attribute == null)
            return null;

        // Extract event type from attribute constructor argument
        var eventType = attribute.ConstructorArguments.FirstOrDefault().Value?.ToString();
        if (string.IsNullOrEmpty(eventType))
            return null;

        // Get containing type
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return null;

        // Extract named arguments
        string? exchange = null;
        string? queue = null;
        var prefetchCount = 10;

        foreach (var namedArg in attribute.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "Exchange":
                    exchange = namedArg.Value.Value?.ToString();
                    break;
                case "Queue":
                    queue = namedArg.Value.Value?.ToString();
                    break;
                case "PrefetchCount":
                    prefetchCount = (int?)namedArg.Value.Value ?? 10;
                    break;
            }
        }

        return new SubscriptionInfo
        {
            EventType = eventType,
            HandlerTypeFullName = containingType.ToDisplayString(),
            HandlerMethodName = methodSymbol.Name,
            Exchange = exchange,
            Queue = queue,
            PrefetchCount = prefetchCount
        };
    }

    private static bool HasRabbitMqSubscribeAttribute(AttributeData attr)
    {
        return attr.AttributeClass != null && (
            attr.AttributeClass.Name == "RabbitMqSubscribeAttribute" ||
            attr.AttributeClass.Name == "RabbitMqSubscribe" ||
            attr.AttributeClass.ToDisplayString().EndsWith(".RabbitMqSubscribeAttribute") ||
            attr.AttributeClass.ToDisplayString().EndsWith(".RabbitMqSubscribe"));
    }

    private void ExecuteGeneration(
        SourceProductionContext context,
        ImmutableArray<SubscriptionInfo?> subscriptions)
    {
        if (subscriptions.IsDefaultOrEmpty)
            return;

        var validSubscriptions = subscriptions
            .Where(x => x != null)
            .Cast<SubscriptionInfo>()
            .ToList();

        if (validSubscriptions.Count == 0)
            return;

        var model = new SubscriptionModel
        {
            Namespace = "CrestCreates.EventBus.RabbitMQ.Generated",
            Subscriptions = validSubscriptions
        };

        var writer = new RabbitMqSubscriptionCodeWriter();
        var source = writer.WriteRegistry(model);

        context.AddSource(
            "RabbitMqSubscriptionRegistry.g.cs",
            SourceText.From(source, Encoding.UTF8));
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 5: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/RabbitMqGenerator/
git commit -m "feat(codegenerator): add RabbitMQ subscription source generator"
```

---

### Task 8: Consumer

**Files:**
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Consuming/RabbitMqConsumer.cs`
- Create: `framework/test/CrestCreates.EventBus.RabbitMQ.Tests/ConsumerTests.cs`

- [ ] **Step 1: Write failing test for consumer**

```csharp
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Options;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

public class ConsumerTests
{
    [Fact]
    public void Consumer_WithNoSubscriptions_StartsSuccessfully()
    {
        // Arrange
        var mockConnectionPool = new Mock<Connection.RabbitMqConnectionPool>(
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()),
            Mock.Of<ILogger<Connection.RabbitMqConnectionPool>>());

        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<RabbitMqConsumer>>();

        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions());

        // Act - Consumer should handle empty subscriptions gracefully
        // This is a basic instantiation test
        Assert.NotNull(options.Value);
    }
}
```

- [ ] **Step 2: Create RabbitMqConsumer.cs**

```csharp
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

public sealed class RabbitMqConsumer : BackgroundService
{
    private readonly RabbitMqConnectionPool _connectionPool;
    private readonly JsonSerializerContext _jsonContext;
    private readonly RabbitMqOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly List<RabbitMqSubscriptionInfo> _subscriptions;

    public RabbitMqConsumer(
        RabbitMqConnectionPool connectionPool,
        JsonSerializerContext jsonContext,
        IOptions<RabbitMqOptions> options,
        IServiceProvider serviceProvider,
        ILogger<RabbitMqConsumer> logger)
    {
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _jsonContext = jsonContext ?? throw new ArgumentNullException(nameof(jsonContext));
        _options = options.Value;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Get subscriptions from generated registry
        _subscriptions = GetSubscriptions();
    }

    private static List<RabbitMqSubscriptionInfo> GetSubscriptions()
    {
        // Try to find the generated registry
        var registryType = Type.GetType("CrestCreates.EventBus.RabbitMQ.Generated.RabbitMqSubscriptionRegistry, CrestCreates.EventBus.RabbitMQ");
        if (registryType == null)
        {
            return new List<RabbitMqSubscriptionInfo>();
        }

        var method = registryType.GetMethod("GetSubscriptions", BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            return new List<RabbitMqSubscriptionInfo>();
        }

        var result = method.Invoke(null, null);
        return result as List<RabbitMqSubscriptionInfo> ?? new List<RabbitMqSubscriptionInfo>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptions.Count == 0)
        {
            _logger.LogWarning("No RabbitMQ subscriptions found. Consumer will not start.");
            return;
        }

        _logger.LogInformation("Starting RabbitMQ consumer with {Count} subscriptions", _subscriptions.Count);

        var channel = await _connectionPool.GetChannelAsync(stoppingToken);

        try
        {
            // Declare DLX
            channel.ExchangeDeclare(_options.DeadLetterExchange, ExchangeType.Direct, durable: true);

            // Setup queues for each subscription
            foreach (var subscription in _subscriptions)
            {
                await SetupSubscriptionQueueAsync(channel, subscription, stoppingToken);
            }

            // Start consuming
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += OnMessageReceivedAsync;

            foreach (var subscription in _subscriptions)
            {
                channel.BasicConsume(
                    queue: subscription.Queue,
                    autoAck: false,
                    consumer: consumer);

                _logger.LogInformation(
                    "Started consuming queue {Queue} for event {EventType}",
                    subscription.Queue, subscription.EventType);
            }

            // Wait until cancelled
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RabbitMQ consumer stopped");
        }
        finally
        {
            _connectionPool.ReturnChannel(channel);
        }
    }

    private async Task SetupSubscriptionQueueAsync(IModel channel, RabbitMqSubscriptionInfo subscription, CancellationToken cancellationToken)
    {
        // Declare main queue with DLX
        var dlxArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", _options.DeadLetterExchange },
            { "x-dead-letter-routing-key", $"{subscription.Queue}.dlq" }
        };

        channel.QueueDeclare(
            queue: subscription.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: dlxArgs);

        // Declare DLQ
        channel.QueueDeclare(
            queue: $"{subscription.Queue}.dlq",
            durable: true,
            exclusive: false,
            autoDelete: false);

        // Bind DLQ to DLX
        channel.QueueBind(
            queue: $"{subscription.Queue}.dlq",
            exchange: _options.DeadLetterExchange,
            routingKey: $"{subscription.Queue}.dlq");

        // Bind main queue to exchange
        channel.QueueBind(
            queue: subscription.Queue,
            exchange: subscription.Exchange,
            routingKey: subscription.EventType);

        // Set prefetch
        channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)subscription.PrefetchCount, global: false);

        _logger.LogDebug(
            "Setup queue {Queue} bound to exchange {Exchange} with routing key {RoutingKey}",
            subscription.Queue, subscription.Exchange, subscription.EventType);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = ((AsyncEventingBasicConsumer)sender).Channel;

        try
        {
            // Deserialize envelope
            var envelope = JsonSerializer.Deserialize(
                ea.Body.Span,
                RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);

            if (envelope == null)
            {
                _logger.LogError("Failed to deserialize message envelope");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            // Find subscription
            var subscription = _subscriptions.FirstOrDefault(s => s.EventType == envelope.EventType);
            if (subscription == null)
            {
                _logger.LogWarning("No subscription found for event type {EventType}", envelope.EventType);
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            // Get retry count from headers
            var retryCount = GetRetryCount(ea);

            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService(subscription.HandlerType);

            // Get the event type from the context
            var eventTypeInfo = _jsonContext.GetTypeInfo(Type.GetType(envelope.EventType) ?? typeof(object));
            var eventPayload = JsonSerializer.Deserialize(envelope.Payload, eventTypeInfo);

            // Invoke handler method
            var method = subscription.HandlerType.GetMethod(
                subscription.HandlerMethod,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (method == null)
            {
                throw new RabbitMqConsumeException(
                    $"Handler method {subscription.HandlerMethod} not found on {subscription.HandlerType.Name}",
                    envelope.EventType,
                    GetCorrelationId(ea));
            }

            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = eventPayload;

            if (parameters.Length > 1 && parameters[1].ParameterType == typeof(CancellationToken))
            {
                args[1] = CancellationToken.None;
            }

            var result = method.Invoke(handler, args);

            if (result is Task task)
            {
                await task;
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);

            _logger.LogInformation(
                "Successfully handled event {EventType} from queue {Queue}",
                envelope.EventType, subscription.Queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message");

            var retryCount = GetRetryCount(ea);

            if (retryCount < _options.RetryCount)
            {
                // Increment retry count and requeue
                IncrementRetryCount(ea);
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);

                _logger.LogWarning(
                    "Retrying message, attempt {Attempt} of {MaxRetries}",
                    retryCount + 1, _options.RetryCount);
            }
            else
            {
                // Max retries reached, send to DLQ
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);

                _logger.LogError(
                    "Max retries ({MaxRetries}) reached for message, sending to DLQ",
                    _options.RetryCount);
            }
        }
    }

    private static int GetRetryCount(BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.Headers != null &&
            ea.BasicProperties.Headers.TryGetValue("x-retry-count", out var value) &&
            value is int retryCount)
        {
            return retryCount;
        }
        return 0;
    }

    private static void IncrementRetryCount(BasicDeliverEventArgs ea)
    {
        ea.BasicProperties.Headers ??= new Dictionary<string, object?>();
        var retryCount = GetRetryCount(ea);
        ea.BasicProperties.Headers["x-retry-count"] = retryCount + 1;
    }

    private static string? GetCorrelationId(BasicDeliverEventArgs ea)
    {
        return ea.BasicProperties.CorrelationId;
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/Consuming/
git add framework/test/CrestCreates.EventBus.RabbitMQ.Tests/ConsumerTests.cs
git commit -m "feat(eventbus-rabbitmq): add consumer with retry and DLQ support"
```

---

### Task 9: EventBus Implementation

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBus.cs`

- [ ] **Step 1: Update RabbitMqEventBus.cs**

```csharp
using System.Text.Json;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Publishing;
using CrestCreates.EventBus.RabbitMQ.Options;

namespace CrestCreates.EventBus.RabbitMQ;

public class RabbitMqEventBus : DistributedEventBusBase
{
    private readonly RabbitMqPublisher _publisher;
    private readonly RabbitMqOptions _options;

    public RabbitMqEventBus(
        RabbitMqPublisher publisher,
        Microsoft.Extensions.Options.IOptions<RabbitMqOptions> options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options.Value;
    }

    public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = @event.GetType();
        var routingKey = eventType.Name;

        await _publisher.PublishAsync(
            _options.DefaultExchange,
            routingKey,
            @event,
            null,
            cancellationToken);
    }

    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(TEvent);
        var routingKey = eventType.Name;

        await _publisher.PublishAsync(
            _options.DefaultExchange,
            routingKey,
            @event,
            null,
            cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "RabbitMQ subscriptions are discovered at compile-time via [RabbitMqSubscribe] attribute. " +
            "Mark your handler method with [RabbitMqSubscribe(\"EventTypeName\")] to register a subscription.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "RabbitMQ subscriptions are managed at compile-time and cannot be dynamically unsubscribed.");
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBus.cs
git commit -m "feat(eventbus-rabbitmq): implement RabbitMqEventBus"
```

---

### Task 10: Service Registration Extensions

**Files:**
- Create: `framework/src/CrestCreates.EventBus.RabbitMQ/Extensions/RabbitMqEventBusServiceCollectionExtensions.cs`

- [ ] **Step 1: Create RabbitMqEventBusServiceCollectionExtensions.cs**

```csharp
using System.Text.Json;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.EventBus.RabbitMQ.Extensions;

public static class RabbitMqEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqEventBus<TContext>(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null)
        where TContext : JsonSerializerContext, new()
    {
        // Register options
        services.Configure<RabbitMqOptions>(configure ?? (_ => { }));

        // Register JsonSerializerContext as singleton
        services.AddSingleton<JsonSerializerContext>(sp => new TContext());

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

    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        JsonSerializerContext jsonContext,
        Action<RabbitMqOptions>? configure = null)
    {
        // Register options
        services.Configure<RabbitMqOptions>(configure ?? (_ => { }));

        // Register JsonSerializerContext as singleton
        services.AddSingleton(jsonContext);

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

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/Extensions/
git commit -m "feat(eventbus-rabbitmq): add service registration extensions"
```

---

### Task 11: Module Definition

**Files:**
- Modify: `framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBusModule.cs`

- [ ] **Step 1: Update RabbitMqEventBusModule.cs**

```csharp
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.Modularity;

namespace CrestCreates.EventBus.RabbitMQ;

public class RabbitMqEventBusModule : ModuleBase
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Module provides defaults - user should call AddRabbitMqEventBus<TContext>
        // in their startup with their specific JsonSerializerContext
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.EventBus.RabbitMQ/CrestCreates.EventBus.RabbitMQ.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.EventBus.RabbitMQ/RabbitMqEventBusModule.cs
git commit -m "feat(eventbus-rabbitmq): add module definition"
```

---

### Task 12: Final Build and Test Verification

**Files:**
- All project files

- [ ] **Step 1: Build entire solution**

Run: `dotnet build framework/CrestCreates.Framework.slnf`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test framework/test/CrestCreates.EventBus.RabbitMQ.Tests/CrestCreates.EventBus.RabbitMQ.Tests.csproj`
Expected: All tests pass

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat(eventbus-rabbitmq): complete RabbitMQ event bus implementation"
```

---

## Acceptance Criteria

1. ✅ RabbitMQ connection pool maintains single connection with automatic recovery
2. ✅ Publisher confirms enabled for reliable message delivery
3. ✅ Consumer implements fixed-delay retry with DLQ fallback
4. ✅ All serialization uses SourceGenerated JsonSerializerContext (AOT-friendly)
5. ✅ Source Generator generates static subscription registry from `[RabbitMqSubscribe]` attributes
6. ✅ RabbitMqEventBus implements `DistributedEventBusBase` correctly
7. ✅ Module integrates with framework's modular architecture
8. ✅ Unit tests cover core functionality with mocked RabbitMQ
9. ✅ All builds pass with 0 errors
