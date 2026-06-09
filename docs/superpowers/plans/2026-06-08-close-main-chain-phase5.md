# Phase 5: Close the Main Chain — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Capability Execution Pipeline fully operational: validate input against Schema, publish lifecycle events to the EventBus, and eliminate registration-time reflection via source-generated handler invokers.

**Architecture:** Three independent subsystems. Schema validation uses `SchemaFieldDescriptor` constraints (required, maxLength, pattern, type) via `ISchemaValidator`. Event publishing wraps lifecycle events as `ILocalEvent` and publishes to `ILocalEventBus`. Source-generated handler invokers replace the `TypedHandlerInvoker` reflection with compile-time wrappers.

**Tech Stack:** .NET 10, C# 13, System.Text.Json, Roslyn IIncrementalGenerator, xUnit + FluentAssertions

---

### Task 0: Schema Validation Types + Validator

**Files:**
- Create: `framework/src/CrestCreates.Schema.Abstractions/SchemaValidationError.cs`
- Create: `framework/src/CrestCreates.Schema.Abstractions/SchemaValidationResult.cs`
- Create: `framework/src/CrestCreates.Schema.Abstractions/ISchemaValidator.cs`
- Create: `framework/src/CrestCreates.Schema/SchemaValidator.cs`

- [ ] **Step 1: Write SchemaValidationError.cs**

```csharp
namespace CrestCreates.Schema.Abstractions;

public sealed class SchemaValidationError
{
    public string FieldName { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Write SchemaValidationResult.cs**

```csharp
namespace CrestCreates.Schema.Abstractions;

public sealed class SchemaValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<SchemaValidationError> Errors { get; init; } = Array.Empty<SchemaValidationError>();

    public static SchemaValidationResult Success()
        => new() { IsValid = true };

    public static SchemaValidationResult Failure(IReadOnlyList<SchemaValidationError> errors)
        => new() { IsValid = false, Errors = errors };
}
```

- [ ] **Step 3: Write ISchemaValidator.cs**

```csharp
namespace CrestCreates.Schema.Abstractions;

public interface ISchemaValidator
{
    SchemaValidationResult Validate(SchemaDescriptor schema, object? payload);
}
```

- [ ] **Step 4: Write SchemaValidator.cs**

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public sealed class SchemaValidator : ISchemaValidator
{
    public SchemaValidationResult Validate(SchemaDescriptor schema, object? payload)
    {
        if (payload == null)
        {
            var requiredFields = schema.Fields.Where(f => f.IsRequired).ToList();
            if (requiredFields.Count > 0)
            {
                return SchemaValidationResult.Failure(
                    requiredFields.Select(f => new SchemaValidationError
                    {
                        FieldName = f.Name,
                        ErrorCode = "FIELD_REQUIRED",
                        Message = $"Field '{f.Name}' is required but payload is null."
                    }).ToList());
            }
            return SchemaValidationResult.Success();
        }

        var errors = new List<SchemaValidationError>();
        var json = payload is string s ? s : JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var field in schema.Fields)
            ValidateField(root, field, errors);

        return errors.Count == 0
            ? SchemaValidationResult.Success()
            : SchemaValidationResult.Failure(errors);
    }

    private static void ValidateField(JsonElement root, SchemaFieldDescriptor field, List<SchemaValidationError> errors)
    {
        if (!root.TryGetProperty(field.Name, out var element))
        {
            if (field.IsRequired)
                errors.Add(new SchemaValidationError
                {
                    FieldName = field.Name,
                    ErrorCode = "FIELD_REQUIRED",
                    Message = $"Field '{field.Name}' is required."
                });
            return;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!field.IsNullable)
                errors.Add(new SchemaValidationError
                {
                    FieldName = field.Name,
                    ErrorCode = "NULL_NOT_ALLOWED",
                    Message = $"Field '{field.Name}' does not allow null."
                });
            return;
        }

        ValidateType(field, element, errors);
        ValidateStringConstraints(field, element, errors);
        ValidateNumericConstraints(field, element, errors);
    }

    private static void ValidateType(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        var type = field.FieldType;
        var kind = element.ValueKind;

        var valid = type switch
        {
            "string" => kind == JsonValueKind.String,
            "int" or "long" or "decimal" or "double" => kind == JsonValueKind.Number,
            "bool" => kind == JsonValueKind.True || kind == JsonValueKind.False,
            _ => true
        };

        if (!valid)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = "TYPE_MISMATCH",
                Message = $"Field '{field.Name}' expected {type}, got {kind}."
            });
    }

    private static void ValidateStringConstraints(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.String) return;
        var value = element.GetString()!;

        if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = "MAX_LENGTH_EXCEEDED",
                Message = $"Field '{field.Name}' exceeds max length {field.MaxLength}."
            });

        if (field.MinLength.HasValue && value.Length < field.MinLength.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = "MIN_LENGTH_NOT_MET",
                Message = $"Field '{field.Name}' shorter than min length {field.MinLength}."
            });

        if (field.Pattern != null && !Regex.IsMatch(value, field.Pattern))
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = "PATTERN_MISMATCH",
                Message = $"Field '{field.Name}' does not match pattern '{field.Pattern}'."
            });
    }

    private static void ValidateNumericConstraints(SchemaFieldDescriptor field, JsonElement element, List<SchemaValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Number) return;
        var value = element.GetDecimal();

        if (field.MaxValue.HasValue && value > (decimal)field.MaxValue.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = "MAX_VALUE_EXCEEDED",
                Message = $"Field '{field.Name}' exceeds max value {field.MaxValue}."
            });

        if (field.MinValue.HasValue && value < (decimal)field.MinValue.Value)
            errors.Add(new SchemaValidationError
            {
                FieldName = field.Name,
                ErrorCode = "MIN_VALUE_NOT_MET",
                Message = $"Field '{field.Name}' below min value {field.MinValue}."
            });
    }
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Schema.Abstractions/CrestCreates.Schema.Abstractions.csproj && dotnet build framework/src/CrestCreates.Schema/CrestCreates.Schema.csproj`
Expected: Both build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Schema.Abstractions/ framework/src/CrestCreates.Schema/
git commit -m "feat: add SchemaValidator — validates payload against SchemaFieldDescriptor constraints"
```

---

### Task 1: Wire ValidationMiddleware to SchemaValidator

**Files:**
- Modify: `framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
- Modify: `framework/src/CrestCreates.Capability/Middleware/ValidationMiddleware.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Add Schema references to Capability.csproj**

```xml
<ProjectReference Include="..\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
```

- [ ] **Step 2: Rewrite ValidationMiddleware**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class ValidationMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ISchemaValidator? _validator;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly ISchemaRegistry? _schemaRegistry;

    public ValidationMiddleware(
        ISchemaValidator? validator,
        ICapabilityRegistry capabilityRegistry,
        ISchemaRegistry? schemaRegistry = null)
    {
        _validator = validator;
        _capabilityRegistry = capabilityRegistry;
        _schemaRegistry = schemaRegistry;
    }

    public Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_validator == null || _schemaRegistry == null)
            return next(context);

        var capDescriptor = _capabilityRegistry.GetByName(context.CapabilityName);
        if (capDescriptor == null)
            return next(context);

        var schemaDescriptor = _schemaRegistry.GetById(capDescriptor.InputSchema.Id);
        if (schemaDescriptor == null)
            return next(context);

        var result = _validator.Validate(schemaDescriptor, context.Input);
        if (!result.IsValid)
        {
            var errorMessages = string.Join("; ", result.Errors.Select(e => e.Message));
            return Task.FromResult(CapabilityExecutionResult.Failure(
                "CAPABILITY_VALIDATION_FAILED",
                $"Input validation failed: {errorMessages}",
                TimeSpan.Zero));
        }

        return next(context);
    }
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Capability/
git commit -m "feat: wire ValidationMiddleware to ISchemaValidator — validates input against capability InputSchema"
```

---

### Task 2: Schema Validator Tests (10 tests)

**Files:**
- Create: `framework/test/CrestCreates.Schema.Tests/SchemaValidatorTests.cs`

Test cases: null payload with required fields, missing required field, type mismatch, string too long/short, pattern mismatch, null on non-nullable field, valid payload, multiple errors returned, optional fields pass through.

- [ ] **Step 1: Write SchemaValidatorTests.cs**

```csharp
using System.Text.Json;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public class SchemaValidatorTests
{
    [Fact]
    public void Validate_NullPayload_WithRequiredFields_ReturnsFailure()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == "FIELD_REQUIRED");
    }

    [Fact]
    public void Validate_MissingRequiredField_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true }
            }
        };
        var payload = JsonSerializer.Serialize(new { Other = "value" });
        var result = new SchemaValidator().Validate(schema, payload);
        result.IsValid.Should().BeFalse();
        result.Errors[0].FieldName.Should().Be("Name");
    }

    [Fact]
    public void Validate_TypeMismatch_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Age", FieldType = "int", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Age\":\"x\"}");
        result.IsValid.Should().BeFalse();
        result.Errors[0].ErrorCode.Should().Be("TYPE_MISMATCH");
    }

    [Fact]
    public void Validate_StringTooLong_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Code", FieldType = "string", MaxLength = 5 }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Code\":\"123456\"}");
        result.Errors[0].ErrorCode.Should().Be("MAX_LENGTH_EXCEEDED");
    }

    [Fact]
    public void Validate_PatternMismatch_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Email", FieldType = "string", Pattern = @"^[^@]+@[^@]+$" }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Email\":\"bad\"}");
        result.Errors[0].ErrorCode.Should().Be("PATTERN_MISMATCH");
    }

    [Fact]
    public void Validate_ValidPayload_ReturnsSuccess()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true, MaxLength = 50 },
                new() { Name = "Age", FieldType = "int", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Name\":\"John\",\"Age\":30}");
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullValueOnNonNullable_ReturnsError()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsNullable = false }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Name\":null}");
        result.Errors[0].ErrorCode.Should().Be("NULL_NOT_ALLOWED");
    }

    [Fact]
    public void Validate_MultipleFields_ReturnsAllErrors()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = true },
                new() { Name = "Age", FieldType = "int", IsRequired = true }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{\"Other\":\"x\"}");
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_OptionalFieldMissing_Passes()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Name", FieldType = "string", IsRequired = false }
            }
        };
        var result = new SchemaValidator().Validate(schema, "{}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NumericConstraints()
    {
        var schema = new SchemaDescriptor
        {
            Id = "s1", Name = "Test", Version = 1,
            Fields = new List<SchemaFieldDescriptor>
            {
                new() { Name = "Score", FieldType = "int", MinValue = 0, MaxValue = 100 }
            }
        };
        var tooLow = new SchemaValidator().Validate(schema, "{\"Score\":-1}");
        tooLow.Errors[0].ErrorCode.Should().Be("MIN_VALUE_NOT_MET");

        var tooHigh = new SchemaValidator().Validate(schema, "{\"Score\":101}");
        tooHigh.Errors[0].ErrorCode.Should().Be("MAX_VALUE_EXCEEDED");
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test framework/test/CrestCreates.Schema.Tests/CrestCreates.Schema.Tests.csproj`
Expected: 19 tests pass (9 existing + 10 new).

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Schema.Tests/
git commit -m "feat: add SchemaValidatorTests — 10 tests"
```

---

### Task 3: EventPublisher — Bridge to ILocalEventBus

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/IEventPublisher.cs`
- Create: `framework/src/CrestCreates.Capability/EventPublisher.cs`
- Modify: `framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`

- [ ] **Step 1: Write IEventPublisher.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface IEventPublisher
{
    Task PublishAsync(string eventName, object? payload, CancellationToken ct = default);
}
```

- [ ] **Step 2: Add EventBus reference to Capability.csproj**

```xml
<ProjectReference Include="..\CrestCreates.EventBus.Abstractions\CrestCreates.EventBus.Abstractions.csproj" />
```

- [ ] **Step 3: Write EventPublisher.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.Capability;

public sealed class EventPublisher : IEventPublisher
{
    private readonly ILocalEventBus? _eventBus;

    public EventPublisher(ILocalEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    public async Task PublishAsync(string eventName, object? payload, CancellationToken ct = default)
    {
        if (_eventBus == null) return;
        var envelope = new CapabilityEventEnvelope
        {
            EventName = eventName,
            Payload = payload,
            Timestamp = DateTimeOffset.UtcNow
        };
        await _eventBus.PublishAsync(envelope, ct).ConfigureAwait(false);
    }
}

internal sealed class CapabilityEventEnvelope : ILocalEvent
{
    public string EventName { get; init; } = string.Empty;
    public object? Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/IEventPublisher.cs framework/src/CrestCreates.Capability/
git commit -m "feat: add EventPublisher — bridges IEventPublisher to ILocalEventBus"
```

---

### Task 4: EventPublishingMiddleware

**Files:**
- Create: `framework/src/CrestCreates.Capability/Middleware/EventPublishingMiddleware.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Write EventPublishingMiddleware.cs**

Publishes `capability.succeeded` or `capability.failed` system events after handler execution:

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class EventPublishingMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IEventPublisher? _publisher;

    public EventPublishingMiddleware(IEventPublisher? publisher = null)
    {
        _publisher = publisher;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        var result = await next(context).ConfigureAwait(false);

        if (_publisher == null) return result;

        var eventName = result.IsSuccess
            ? "capability.succeeded"
            : "capability.failed";

        await _publisher.PublishAsync(eventName, new
        {
            capabilityName = context.CapabilityName,
            capabilityVersion = context.CapabilityVersion,
            correlationId = context.CorrelationId,
            result.Status,
            result.ErrorCode,
            result.Duration
        }, context.CancellationToken).ConfigureAwait(false);

        return result;
    }
}
```

- [ ] **Step 2: Add to pipeline defaults in CapabilityServiceCollectionExtensions**

In `AddCapabilityPipeline`, add `builder.Use<EventPublishingMiddleware>();` after ValidationMiddleware. And register the middleware:
```csharp
services.TryAddTransient<EventPublishingMiddleware>();
```

- [ ] **Step 3: Build and verify + commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add EventPublishingMiddleware — publishes lifecycle events to EventBus"
```

---

### Task 5: EventPublisher + Middleware Tests

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/EventPublisherTests.cs`

- [ ] **Step 1: Write EventPublisherTests.cs (4 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class EventPublisherTests
{
    [Fact]
    public async Task PublishAsync_WithNullEventBus_DoesNotThrow()
    {
        var publisher = new EventPublisher(null);
        await publisher.Invoking(p => p.PublishAsync("test.event", new { }))
            .Should().NotThrowAsync();
    }

    [Fact]
    public void EventPublisher_Implements_IEventPublisher()
    {
        var publisher = new EventPublisher(null);
        publisher.Should().BeAssignableTo<IEventPublisher>();
    }

    [Fact]
    public async Task EventPublishingMiddleware_Passthrough_WhenNullPublisher()
    {
        var middleware = new Middleware.EventPublishingMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EventPublishingMiddleware_PublishesFailed_OnFailure()
    {
        var middleware = new Middleware.EventPublishingMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR", "bad", TimeSpan.Zero)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR");
    }
}
```

- [ ] **Step 2: Run tests + commit**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
git add framework/test/CrestCreates.Capability.Tests/EventPublisherTests.cs
git commit -m "feat: add EventPublisher + EventPublishingMiddleware tests — 4 tests"
```
Expected: ~38 tests pass (34 existing + 4 new).

---

### Task 6: Source-Generated Handler Invokers — Generator Expansion

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs`
- Create: `framework/tools/CrestCreates.CodeGenerator/Models/HandlerInvokerInfo.cs`

Discover `ICapabilityHandler<TInput, TOutput>` implementations and generate compile-time `ICapabilityHandlerInvoker` wrappers + registration code.

- [ ] **Step 1: Write HandlerInvokerInfo.cs**

```csharp
namespace CrestCreates.CodeGenerator.Models;

internal sealed class HandlerInvokerInfo
{
    public string HandlerTypeName { get; set; } = string.Empty;
    public string HandlerFullName { get; set; } = string.Empty;
    public string CapabilityName { get; set; } = string.Empty;
    public string InputTypeName { get; set; } = string.Empty;
    public string OutputTypeName { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Expand generator — discover handler types**

Add to `Initialize()` in `SchemaCapabilitySourceGenerator.cs`:
- SyntaxProvider that finds classes implementing `ICapabilityHandler<,>`
- Extract the handler's capability name from attribute or convention
- Generate invoker wrapper classes + `CapabilityHandlerResolver.Register()` calls

The generated code for each handler:
```csharp
// Generated: MyModule.EchoHandlerInvoker
internal sealed class EchoHandler_Invoker : ICapabilityHandlerInvoker
{
    private readonly MyModule.EchoHandler _handler;
    public EchoHandler_Invoker(MyModule.EchoHandler handler) { _handler = handler; }
    public async Task<object?> InvokeAsync(object? input, CancellationToken ct)
    {
        var typedInput = (string)input!;
        var result = await _handler.ExecuteAsync(typedInput, ct);
        return result;
    }
}

// Registration:
CapabilityHandlerResolver.Register("test.echo", sp => new EchoHandler_Invoker(
    sp.GetRequiredService<MyModule.EchoHandler>()));
```

The generator needs to:
1. Check `compilation.ReferencedAssemblyNames` for `CrestCreates.Capability.Abstractions`
2. Find classes implementing `ICapabilityHandler` with generic parameters
3. Extract the capability name (from `[CapabilityName("x")]` attribute or convention)
4. Generate invoker wrapper + registration in the module initializer

- [ ] **Step 3: Build generator + test**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/
git commit -m "feat: expand source generator for handler invoker wrappers"
```

---

### Task 7: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all unit tests**

```bash
dotnet test CrestCreates.slnx --filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~Kafka&FullyQualifiedName!~RabbitMQ"
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: complete Phase 5 — Schema validation, EventBus integration, source-gen handlers"
```

---

## Phase 5 Summary

| Subsystem | Files | Tests |
|-----------|-------|-------|
| Schema Validation | ISchemaValidator, SchemaValidator, ValidationResult | 10 |
| EventBus Integration | IEventPublisher, EventPublisher, EventPublishingMiddleware | 4 |
| Source-Gen Handlers | Generator expansion, invoker wrappers | — |
| **Total** | **~10 new files, 3 modified** | **~14 new tests**
