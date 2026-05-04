# Global Exception Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one framework-level global exception handling mainline with stable error codes, localized public messages, safe details, trace ids, and consistent JSON output across MVC, generated Dynamic API, and generated CRUD.

**Architecture:** Put the platform exception model in `CrestCreates.Domain.Shared`, align existing domain and permission exceptions to it, and keep `CrestCreates.AspNetCore.Middlewares.ExceptionHandlingMiddleware` as the only runtime middleware. Move mapping/localization/safety logic into an `ICrestExceptionConverter` so the middleware stays small and the mapping is directly testable.

**Tech Stack:** .NET 10, ASP.NET Core middleware, System.Text.Json, CrestCreates.Localization `ILocalizationService`, xUnit, FluentAssertions.

---

## File Structure

| Path | Action | Responsibility |
|---|---|---|
| `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestException.cs` | Create | Platform exception base with `ErrorCode`, `HttpStatusCode`, `Details`, and safe message. |
| `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestBusinessException.cs` | Create | Business-rule failures with caller-provided stable error code. |
| `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestValidationException.cs` | Create | Validation failures with optional error list. |
| `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestEntityNotFoundException.cs` | Create | Entity-not-found platform exception. |
| `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs` | Modify | Inherit from `CrestException`, keep current public constructors and properties. |
| `framework/src/CrestCreates.Domain/Exceptions/CrestPreconditionRequiredException.cs` | Modify | Inherit from `CrestException`, keep current public constructors and properties. |
| `framework/src/CrestCreates.Domain/Exceptions/CrestPermissionException.cs` | Modify | Align the older domain permission exception with `CrestException` so it maps to the same 403 contract. |
| `framework/src/CrestCreates.Authorization.Abstractions/CrestPermissionException.cs` | Modify | Inherit from `CrestException`, keep current permission properties. |
| `framework/src/CrestCreates.AspNetCore/CrestCreates.AspNetCore.csproj` | Modify | Add project reference to `CrestCreates.Localization`. |
| `framework/src/CrestCreates.AspNetCore/Errors/CrestErrorResponse.cs` | Create | Public JSON error response contract. |
| `framework/src/CrestCreates.AspNetCore/Errors/CrestExceptionConversionResult.cs` | Create | Internal converter result with response and log level. |
| `framework/src/CrestCreates.AspNetCore/Errors/ICrestExceptionConverter.cs` | Create | Converter abstraction. |
| `framework/src/CrestCreates.AspNetCore/Errors/DefaultCrestExceptionConverter.cs` | Create | Type-based exception mapping, localization, fallback, and safe details. |
| `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs` | Modify | Delegate to converter, write `CrestErrorResponse`, use app JSON options, log by result level. |
| `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddlewareExtensions.cs` | Create if split is chosen | Keep `UseExceptionHandling` extension if the middleware file becomes too large. |
| `framework/src/CrestCreates.Application/Services/CrestAppServiceBase.cs` | Modify | Rethrow platform/known exceptions before generic wrapping. |
| `framework/src/CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs` | Delete | Remove excluded legacy middleware copy. |
| `framework/src/CrestCreates.Web/CrestCreates.Web.csproj` | Modify | Remove now-unneeded `Compile Remove="Middlewares\ExceptionHandlingMiddleware.cs"`. |
| `framework/src/CrestCreates.Domain.Shared/Localization/Resources/zh-CN.json` | Modify | Add exception error-code messages. |
| `framework/src/CrestCreates.Domain.Shared/Localization/Resources/en.json` | Create | Add English exception error-code messages because this file does not currently exist. |
| `framework/test/CrestCreates.Web.Tests/Middlewares/ExceptionHandlingMiddlewareTests.cs` | Modify | Update middleware tests for stable `code`, `statusCode`, localization, and safety. |
| `framework/test/CrestCreates.Web.Tests/Middlewares/DefaultCrestExceptionConverterTests.cs` | Create | Direct converter mapping tests. |
| `framework/test/CrestCreates.IntegrationTests/ConcurrencyIntegrationTests.cs` | Modify | Update 409/428 assertions to new response contract. |
| `framework/test/CrestCreates.Web.Tests/DynamicApi/GeneratedDynamicApiRuntimeTests.cs` | Modify | Add generated runtime exception propagation assertion if needed. |

Do not execute this plan in this session. It is a handoff plan for another implementation agent.

---

### Task 1: Add Platform Exception Base Types

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestException.cs`
- Create: `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestBusinessException.cs`
- Create: `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestValidationException.cs`
- Create: `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestEntityNotFoundException.cs`
- Test: compile checks in later tasks

- [ ] **Step 1: Create `CrestException`**

Create `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestException.cs`:

```csharp
namespace CrestCreates.Domain.Shared.Exceptions;

public abstract class CrestException : Exception
{
    protected CrestException(
        string errorCode,
        int httpStatusCode,
        string message,
        string? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
        Details = details;
    }

    public string ErrorCode { get; }

    public int HttpStatusCode { get; }

    public string? Details { get; }
}
```

- [ ] **Step 2: Create `CrestBusinessException`**

Create `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestBusinessException.cs`:

```csharp
namespace CrestCreates.Domain.Shared.Exceptions;

public class CrestBusinessException : CrestException
{
    public CrestBusinessException(
        string errorCode,
        string message,
        string? details = null,
        Exception? innerException = null)
        : base(errorCode, 400, message, details, innerException)
    {
    }
}
```

- [ ] **Step 3: Create `CrestValidationException`**

Create `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestValidationException.cs`:

```csharp
namespace CrestCreates.Domain.Shared.Exceptions;

public class CrestValidationException : CrestException
{
    public CrestValidationException(
        string message = "Validation failed.",
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base("Crest.Validation.Failed", 400, message, errors is { Count: > 0 } ? string.Join("; ", errors) : null, innerException)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}
```

- [ ] **Step 4: Create `CrestEntityNotFoundException`**

Create `framework/src/CrestCreates.Domain.Shared/Exceptions/CrestEntityNotFoundException.cs`:

```csharp
namespace CrestCreates.Domain.Shared.Exceptions;

public class CrestEntityNotFoundException : CrestException
{
    public CrestEntityNotFoundException(string entityType, object? entityId = null)
        : base(
            "Crest.Entity.NotFound",
            404,
            "Entity not found.",
            entityId is null ? entityType : $"{entityType} (Id={entityId})")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public object? EntityId { get; }
}
```

- [ ] **Step 5: Build Domain.Shared**

Run:

```powershell
dotnet build framework\src\CrestCreates.Domain.Shared\CrestCreates.Domain.Shared.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

Run:

```powershell
git add framework\src\CrestCreates.Domain.Shared\Exceptions
git commit -m "feat: add platform exception base types"
```

---

### Task 2: Align Existing Exceptions To Platform Base

**Files:**
- Modify: `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs`
- Modify: `framework/src/CrestCreates.Domain/Exceptions/CrestPreconditionRequiredException.cs`
- Modify: `framework/src/CrestCreates.Domain/Exceptions/CrestPermissionException.cs`
- Modify: `framework/src/CrestCreates.Authorization.Abstractions/CrestPermissionException.cs`
- Test: `framework/test/CrestCreates.Domain.Tests/ConcurrencyExceptionTests.cs`

- [ ] **Step 1: Update concurrency exception**

Replace the class body in `framework/src/CrestCreates.Domain/Exceptions/CrestConcurrencyException.cs` with:

```csharp
using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Domain.Exceptions;

public class CrestConcurrencyException : CrestException
{
    public CrestConcurrencyException(string entityType, object? entityId)
        : base(
            "Crest.Concurrency.Conflict",
            409,
            "Concurrency conflict.",
            $"{entityType} (Id={entityId}) has been modified by another user.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public object? EntityId { get; }
}
```

- [ ] **Step 2: Update precondition exception**

Replace the class body in `framework/src/CrestCreates.Domain/Exceptions/CrestPreconditionRequiredException.cs` with:

```csharp
using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Domain.Exceptions;

public class CrestPreconditionRequiredException : CrestException
{
    public CrestPreconditionRequiredException(string entityType, object? entityId)
        : base(
            "Crest.Concurrency.PreconditionRequired",
            428,
            "Precondition required.",
            $"DELETE on {entityType} (Id={entityId}) requires If-Match header with current ConcurrencyStamp.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public object? EntityId { get; }
}
```

- [ ] **Step 3: Update authorization permission exception**

Replace `framework/src/CrestCreates.Authorization.Abstractions/CrestPermissionException.cs` with:

```csharp
using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Authorization.Abstractions;

public class CrestPermissionException : CrestException
{
    public CrestPermissionException(string permissionName)
        : this(permissionName, $"Permission denied: {permissionName}")
    {
    }

    public CrestPermissionException(string permissionName, string message)
        : base("Crest.Auth.Forbidden", 403, message, permissionName)
    {
        PermissionName = permissionName;
    }

    public CrestPermissionException(string permissionName, string message, Exception innerException)
        : base("Crest.Auth.Forbidden", 403, message, permissionName, innerException)
    {
        PermissionName = permissionName;
    }

    public string PermissionName { get; }
}
```

- [ ] **Step 4: Update domain permission exception**

Replace `framework/src/CrestCreates.Domain/Exceptions/CrestPermissionException.cs` with:

```csharp
using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Domain.Exceptions;

public class CrestPermissionException : CrestException
{
    public CrestPermissionException(string permissionName)
        : base("Crest.Auth.Forbidden", 403, $"Permission '{permissionName}' was not granted.", permissionName)
    {
        PermissionName = permissionName;
    }

    public string PermissionName { get; }
}
```

This keeps the old namespace compiling while giving it the same platform error semantics. Do not make `CrestCreates.Domain` depend on `CrestCreates.Authorization.Abstractions`.

- [ ] **Step 5: Update existing domain tests if they assert exact messages**

If `framework/test/CrestCreates.Domain.Tests/ConcurrencyExceptionTests.cs` asserts old message text, change it to assert platform properties:

```csharp
[Fact]
public void CrestConcurrencyException_HasPlatformErrorInfo()
{
    var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var ex = new CrestConcurrencyException("Book", id);

    ex.ErrorCode.Should().Be("Crest.Concurrency.Conflict");
    ex.HttpStatusCode.Should().Be(409);
    ex.EntityType.Should().Be("Book");
    ex.EntityId.Should().Be(id);
    ex.Details.Should().Contain("Book");
}
```

- [ ] **Step 6: Run focused builds/tests**

Run:

```powershell
dotnet build framework\src\CrestCreates.Domain\CrestCreates.Domain.csproj --no-restore
dotnet build framework\src\CrestCreates.Authorization.Abstractions\CrestCreates.Authorization.Abstractions.csproj --no-restore
dotnet test framework\test\CrestCreates.Domain.Tests\CrestCreates.Domain.Tests.csproj --no-build --filter "FullyQualifiedName~ConcurrencyException"
```

Expected: builds succeed; focused tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add framework\src\CrestCreates.Domain\Exceptions framework\src\CrestCreates.Authorization.Abstractions\CrestPermissionException.cs framework\test\CrestCreates.Domain.Tests\ConcurrencyExceptionTests.cs
git commit -m "feat: align existing exceptions with platform error model"
```

---

### Task 3: Add Error Response And Converter Tests First

**Files:**
- Create: `framework/test/CrestCreates.Web.Tests/Middlewares/DefaultCrestExceptionConverterTests.cs`

- [ ] **Step 1: Write failing converter tests**

Create `framework/test/CrestCreates.Web.Tests/Middlewares/DefaultCrestExceptionConverterTests.cs`:

```csharp
using CrestCreates.AspNetCore.Errors;
using CrestCreates.Domain.Exceptions;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Localization.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using AuthorizationPermissionException = CrestCreates.Authorization.Abstractions.CrestPermissionException;

namespace CrestCreates.Web.Tests.Middlewares;

public class DefaultCrestExceptionConverterTests
{
    [Fact]
    public void Convert_WithBusinessException_UsesExceptionErrorCode()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext { TraceIdentifier = "trace-business" };
        var exception = new CrestBusinessException("Tenant.AlreadyExists", "Tenant already exists.", "tenant-name");

        var result = converter.Convert(context, exception);

        result.Response.Code.Should().Be("Tenant.AlreadyExists");
        result.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.Response.Message.Should().Be("Tenant already exists.");
        result.Response.Details.Should().Be("tenant-name");
        result.Response.TraceId.Should().Be("trace-business");
    }

    [Fact]
    public void Convert_WithConcurrencyException_Returns409()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext { TraceIdentifier = "trace-409" };

        var result = converter.Convert(context, new CrestConcurrencyException("Book", "b1"));

        result.Response.Code.Should().Be("Crest.Concurrency.Conflict");
        result.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        result.Response.TraceId.Should().Be("trace-409");
        result.LogLevel.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void Convert_WithPreconditionException_Returns428()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext();

        var result = converter.Convert(context, new CrestPreconditionRequiredException("Book", "b1"));

        result.Response.Code.Should().Be("Crest.Concurrency.PreconditionRequired");
        result.Response.StatusCode.Should().Be(428);
    }

    [Fact]
    public void Convert_WithPermissionException_Returns403()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext();

        var result = converter.Convert(context, new AuthorizationPermissionException("Books.Delete"));

        result.Response.Code.Should().Be("Crest.Auth.Forbidden");
        result.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        result.Response.Details.Should().Be("Books.Delete");
    }

    [Fact]
    public void Convert_WithUnhandledException_HidesRawMessage()
    {
        var converter = CreateConverter();
        var context = new DefaultHttpContext { TraceIdentifier = "trace-500" };

        var result = converter.Convert(context, new InvalidCastException("password=secret; stack detail"));

        result.Response.Code.Should().Be("Crest.InternalError");
        result.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        result.Response.Message.Should().Be("服务器内部错误。");
        result.Response.Details.Should().BeNull();
        result.Response.TraceId.Should().Be("trace-500");
        result.LogLevel.Should().Be(LogLevel.Error);
    }

    [Fact]
    public void Convert_WithLocalizedMessage_UsesLocalization()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILocalizationService>(new FakeLocalizationService(new Dictionary<string, string>
            {
                ["Crest.Concurrency.Conflict"] = "本地化并发冲突"
            }))
            .BuildServiceProvider();
        var converter = new DefaultCrestExceptionConverter(services, NullLogger<DefaultCrestExceptionConverter>.Instance);

        var result = converter.Convert(new DefaultHttpContext(), new CrestConcurrencyException("Book", "b1"));

        result.Response.Message.Should().Be("本地化并发冲突");
    }

    private static DefaultCrestExceptionConverter CreateConverter()
    {
        var services = new ServiceCollection()
            .AddSingleton<ILocalizationService>(new FakeLocalizationService(new Dictionary<string, string>()))
            .BuildServiceProvider();
        return new DefaultCrestExceptionConverter(services, NullLogger<DefaultCrestExceptionConverter>.Instance);
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public FakeLocalizationService(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public string CurrentCulture => "zh-CN";

        public string GetString(string key) => _values.TryGetValue(key, out var value) ? value : key;

        public string GetString(string key, params object[] arguments) => string.Format(GetString(key), arguments);

        public string GetString(string key, string cultureName) => GetString(key);

        public string GetString(string key, string cultureName, params object[] arguments) => GetString(key, arguments);

        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(GetString(key));

        public Task<string?> GetStringAsync(string key, params object[] arguments) => Task.FromResult<string?>(GetString(key, arguments));

        public Task<string?> GetStringAsync(string key, string cultureName) => Task.FromResult<string?>(GetString(key));

        public Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments) => Task.FromResult<string?>(GetString(key, arguments));

        public IDisposable ChangeCulture(string cultureName) => new NoopDisposable();

        public Task<IDisposable> ChangeCultureAsync(string cultureName) => Task.FromResult<IDisposable>(new NoopDisposable());

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --no-build --filter "FullyQualifiedName~DefaultCrestExceptionConverterTests"
```

Expected: fail because `CrestCreates.AspNetCore.Errors` types do not exist yet.

- [ ] **Step 3: Commit failing tests**

Run:

```powershell
git add framework\test\CrestCreates.Web.Tests\Middlewares\DefaultCrestExceptionConverterTests.cs
git commit -m "test: specify global exception converter behavior"
```

---

### Task 4: Implement Error Response And Converter

**Files:**
- Modify: `framework/src/CrestCreates.AspNetCore/CrestCreates.AspNetCore.csproj`
- Create: `framework/src/CrestCreates.AspNetCore/Errors/CrestErrorResponse.cs`
- Create: `framework/src/CrestCreates.AspNetCore/Errors/CrestExceptionConversionResult.cs`
- Create: `framework/src/CrestCreates.AspNetCore/Errors/ICrestExceptionConverter.cs`
- Create: `framework/src/CrestCreates.AspNetCore/Errors/DefaultCrestExceptionConverter.cs`

- [ ] **Step 1: Add Localization project reference**

In `framework/src/CrestCreates.AspNetCore/CrestCreates.AspNetCore.csproj`, add:

```xml
<ProjectReference Include="..\CrestCreates.Localization\CrestCreates.Localization.csproj" />
```

inside the existing `ItemGroup` that contains other project references.

- [ ] **Step 2: Create `CrestErrorResponse`**

Create `framework/src/CrestCreates.AspNetCore/Errors/CrestErrorResponse.cs`:

```csharp
namespace CrestCreates.AspNetCore.Errors;

public class CrestErrorResponse
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? TraceId { get; set; }

    public int StatusCode { get; set; }
}
```

- [ ] **Step 3: Create conversion result**

Create `framework/src/CrestCreates.AspNetCore/Errors/CrestExceptionConversionResult.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace CrestCreates.AspNetCore.Errors;

public class CrestExceptionConversionResult
{
    public CrestExceptionConversionResult(CrestErrorResponse response, LogLevel logLevel)
    {
        Response = response;
        LogLevel = logLevel;
    }

    public CrestErrorResponse Response { get; }

    public LogLevel LogLevel { get; }
}
```

- [ ] **Step 4: Create converter interface**

Create `framework/src/CrestCreates.AspNetCore/Errors/ICrestExceptionConverter.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace CrestCreates.AspNetCore.Errors;

public interface ICrestExceptionConverter
{
    CrestExceptionConversionResult Convert(HttpContext context, Exception exception);
}
```

- [ ] **Step 5: Create default converter**

Create `framework/src/CrestCreates.AspNetCore/Errors/DefaultCrestExceptionConverter.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Localization.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.AspNetCore.Errors;

public class DefaultCrestExceptionConverter : ICrestExceptionConverter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DefaultCrestExceptionConverter> _logger;

    public DefaultCrestExceptionConverter(
        IServiceProvider serviceProvider,
        ILogger<DefaultCrestExceptionConverter> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public CrestExceptionConversionResult Convert(HttpContext context, Exception exception)
    {
        var response = exception switch
        {
            CrestPermissionException permissionException => Create(context, "Crest.Auth.Forbidden", 403, "没有权限执行当前操作。", permissionException.PermissionName),
            CrestException crestException => FromCrestException(context, crestException),
            UnauthorizedAccessException => Create(context, "Crest.Auth.Unauthorized", 401, "当前请求未认证。"),
            KeyNotFoundException keyNotFoundException => Create(context, "Crest.Entity.NotFound", 404, "资源不存在。", keyNotFoundException.Message),
            ValidationException validationException => Create(context, "Crest.Validation.Failed", 400, "数据验证失败。", validationException.Message),
            ArgumentException argumentException => Create(context, "Crest.Request.InvalidArgument", 400, "请求参数错误。", argumentException.Message),
            InvalidOperationException invalidOperationException => Create(context, "Crest.Operation.Invalid", 400, "当前操作无效。", invalidOperationException.Message),
            _ => Create(context, "Crest.InternalError", 500, "服务器内部错误。")
        };

        var logLevel = response.StatusCode >= 500 ? LogLevel.Error : LogLevel.Warning;
        return new CrestExceptionConversionResult(response, logLevel);
    }

    private CrestErrorResponse FromCrestException(HttpContext context, CrestException exception)
    {
        return Create(context, exception.ErrorCode, exception.HttpStatusCode, exception.Message, exception.Details);
    }

    private CrestErrorResponse Create(
        HttpContext context,
        string errorCode,
        int statusCode,
        string fallbackMessage,
        string? details = null)
    {
        return new CrestErrorResponse
        {
            Code = errorCode,
            Message = Localize(errorCode, fallbackMessage),
            Details = statusCode >= 500 ? null : details,
            TraceId = context.TraceIdentifier,
            StatusCode = statusCode
        };
    }

    private string Localize(string errorCode, string fallbackMessage)
    {
        var localizationService = _serviceProvider.GetService<ILocalizationService>();
        if (localizationService is null)
        {
            return fallbackMessage;
        }

        try
        {
            var localized = localizationService.GetString(errorCode);
            return string.IsNullOrWhiteSpace(localized) || localized == errorCode
                ? fallbackMessage
                : localized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to localize error code {ErrorCode}", errorCode);
            return fallbackMessage;
        }
    }
}
```

- [ ] **Step 6: Run converter tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~DefaultCrestExceptionConverterTests"
```

Expected: converter tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add framework\src\CrestCreates.AspNetCore\CrestCreates.AspNetCore.csproj framework\src\CrestCreates.AspNetCore\Errors
git commit -m "feat: add global exception converter"
```

---

### Task 5: Refactor Middleware To Use Converter

**Files:**
- Modify: `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs`
- Modify: `framework/test/CrestCreates.Web.Tests/Middlewares/ExceptionHandlingMiddlewareTests.cs`

- [ ] **Step 1: Update middleware tests for new response contract**

Replace assertions in `framework/test/CrestCreates.Web.Tests/Middlewares/ExceptionHandlingMiddlewareTests.cs` so they use `CrestErrorResponse` and stable code strings:

```csharp
response.Code.Should().Be("Crest.Auth.Unauthorized");
response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
response.TraceId.Should().Be("trace-401");
```

Update the helper return type:

```csharp
private static async Task<CrestErrorResponse> DeserializeResponseAsync(HttpContext context)
{
    context.Response.Body.Seek(0, SeekOrigin.Begin);
    var response = await JsonSerializer.DeserializeAsync<CrestErrorResponse>(context.Response.Body);
    response.Should().NotBeNull();
    return response!;
}
```

Add the namespace:

```csharp
using CrestCreates.AspNetCore.Errors;
```

- [ ] **Step 2: Run middleware tests to verify failures**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --no-build --filter "FullyQualifiedName~ExceptionHandlingMiddlewareTests"
```

Expected: fail because middleware constructor and response contract still use old `ErrorResponse`.

- [ ] **Step 3: Replace middleware implementation**

Replace `framework/src/CrestCreates.AspNetCore/Middlewares/ExceptionHandlingMiddleware.cs` with:

```csharp
using System.Runtime.ExceptionServices;
using System.Text.Json;
using CrestCreates.AspNetCore.Errors;
using CrestCreates.Localization.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.AspNetCore.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICrestExceptionConverter _converter;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ICrestExceptionConverter converter,
        ILogger<ExceptionHandlingMiddleware> logger,
        IOptions<JsonOptions>? jsonOptions = null)
    {
        _next = next;
        _converter = converter;
        _logger = logger;
        _jsonSerializerOptions = jsonOptions?.Value.SerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var conversion = _converter.Convert(context, exception);
        var response = conversion.Response;

        LogException(exception, response, conversion.LogLevel);

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write error response because response has already started. TraceId={TraceId}, ErrorCode={ErrorCode}",
                response.TraceId,
                response.Code);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = response.StatusCode;

        await JsonSerializer.SerializeAsync(context.Response.Body, response, _jsonSerializerOptions, context.RequestAborted);
    }

    private void LogException(Exception exception, CrestErrorResponse response, LogLevel logLevel)
    {
        _logger.Log(
            logLevel,
            exception,
            "Request failed with {StatusCode} {ErrorCode}. TraceId={TraceId}",
            response.StatusCode,
            response.Code,
            response.TraceId);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IServiceCollection AddCrestExceptionHandling(this IServiceCollection services)
    {
        services.AddLocalization();
        services.TryAddSingleton<ILocalizationService, LocalizationService>();
        services.TryAddSingleton<ICrestExceptionConverter, DefaultCrestExceptionConverter>();
        return services;
    }

    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
```

- [ ] **Step 4: Register converter in Web startup**

In `framework/src/CrestCreates.Web/Startup.cs`, add this service registration after `services.AddControllers()` or near other platform services:

```csharp
services.AddCrestExceptionHandling();
```

The namespace `CrestCreates.AspNetCore.Middlewares` is already present in `Startup.cs`.

- [ ] **Step 5: Update direct middleware tests to construct dependencies**

In `ExceptionHandlingMiddlewareTests`, create middleware using:

```csharp
private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next, TestLogger<ExceptionHandlingMiddleware> logger)
{
    var services = new ServiceCollection().BuildServiceProvider();
    var converter = new DefaultCrestExceptionConverter(services, NullLogger<DefaultCrestExceptionConverter>.Instance);
    return new ExceptionHandlingMiddleware(next, converter, logger);
}
```

Add namespaces:

```csharp
using CrestCreates.AspNetCore.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
```

- [ ] **Step 6: Run middleware tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~ExceptionHandlingMiddlewareTests|FullyQualifiedName~DefaultCrestExceptionConverterTests"
```

Expected: tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add framework\src\CrestCreates.AspNetCore\Middlewares\ExceptionHandlingMiddleware.cs framework\src\CrestCreates.Web\Startup.cs framework\test\CrestCreates.Web.Tests\Middlewares\ExceptionHandlingMiddlewareTests.cs
git commit -m "feat: route exception middleware through converter"
```

---

### Task 6: Update App Service Exception Whitelist

**Files:**
- Modify: `framework/src/CrestCreates.Application/Services/CrestAppServiceBase.cs`
- Test: existing application tests and build

- [ ] **Step 1: Add Domain.Shared exception namespace**

At the top of `CrestAppServiceBase.cs`, add:

```csharp
using CrestCreates.Domain.Shared.Exceptions;
```

- [ ] **Step 2: Update catch blocks in `CreateAsync`**

Before the `DbException` catch in `CreateAsync`, add:

```csharp
catch (CrestException)
{
    throw;
}
catch (UnauthorizedAccessException)
{
    throw;
}
catch (KeyNotFoundException)
{
    throw;
}
catch (OperationCanceledException)
{
    throw;
}
```

- [ ] **Step 3: Update catch blocks in `UpdateAsync`**

Replace the separate concurrency/precondition catches with this broader whitelist before `DbException`:

```csharp
catch (CrestException)
{
    throw;
}
catch (UnauthorizedAccessException)
{
    throw;
}
catch (KeyNotFoundException)
{
    throw;
}
catch (OperationCanceledException)
{
    throw;
}
```

- [ ] **Step 4: Update catch blocks in `DeleteAsync`**

Apply the same whitelist in `DeleteAsync` before `DbException`.

- [ ] **Step 5: Run application build/tests**

Run:

```powershell
dotnet build framework\src\CrestCreates.Application\CrestCreates.Application.csproj --no-restore
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --no-build
```

Expected: build succeeds; existing application tests pass or only known unrelated failures remain documented by the worker.

- [ ] **Step 6: Commit**

Run:

```powershell
git add framework\src\CrestCreates.Application\Services\CrestAppServiceBase.cs
git commit -m "fix: preserve platform exceptions in app service base"
```

---

### Task 7: Add Localization Resource Keys

**Files:**
- Modify: `framework/src/CrestCreates.Domain.Shared/Localization/Resources/zh-CN.json`
- Create: `framework/src/CrestCreates.Domain.Shared/Localization/Resources/en.json`
- Test: converter localization test from Task 3

- [ ] **Step 1: Add Chinese keys**

Add these entries to `framework/src/CrestCreates.Domain.Shared/Localization/Resources/zh-CN.json`, preserving valid JSON:

```json
{
  "Crest.Auth.Unauthorized": "当前请求未认证。",
  "Crest.Auth.Forbidden": "没有权限执行当前操作。",
  "Crest.Validation.Failed": "数据验证失败。",
  "Crest.Request.InvalidArgument": "请求参数错误。",
  "Crest.Operation.Invalid": "当前操作无效。",
  "Crest.Entity.NotFound": "资源不存在。",
  "Crest.Concurrency.Conflict": "数据已被其他用户修改，请刷新后重试。",
  "Crest.Concurrency.PreconditionRequired": "请求缺少 If-Match 头，请提供当前 ConcurrencyStamp。",
  "Crest.InternalError": "服务器内部错误。"
}
```

- [ ] **Step 2: Add English keys**

Create `framework/src/CrestCreates.Domain.Shared/Localization/Resources/en.json` with:

```json
{
  "Crest.Auth.Unauthorized": "The current request is not authenticated.",
  "Crest.Auth.Forbidden": "You do not have permission to perform this operation.",
  "Crest.Validation.Failed": "Validation failed.",
  "Crest.Request.InvalidArgument": "The request arguments are invalid.",
  "Crest.Operation.Invalid": "The current operation is invalid.",
  "Crest.Entity.NotFound": "The requested resource was not found.",
  "Crest.Concurrency.Conflict": "The data was modified by another user. Refresh and retry.",
  "Crest.Concurrency.PreconditionRequired": "The request requires an If-Match header with the current ConcurrencyStamp.",
  "Crest.InternalError": "An internal server error occurred."
}
```

- [ ] **Step 3: Validate JSON files**

Run:

```powershell
dotnet test framework\test\CrestCreates.Localization.Tests\CrestCreates.Localization.Tests.csproj --no-build
```

Expected: localization tests pass. If the test project does not load these JSON resources directly, at minimum the command should compile successfully.

- [ ] **Step 4: Commit**

Run:

```powershell
git add framework\src\CrestCreates.Domain.Shared\Localization\Resources\zh-CN.json framework\src\CrestCreates.Domain.Shared\Localization\Resources\en.json
git commit -m "feat: add localized exception messages"
```

---

### Task 8: Remove Legacy Web Middleware Copy

**Files:**
- Delete: `framework/src/CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs`
- Modify: `framework/src/CrestCreates.Web/CrestCreates.Web.csproj`

- [ ] **Step 1: Delete excluded middleware file**

Delete:

```text
framework/src/CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs
```

- [ ] **Step 2: Remove redundant compile exclusion**

In `framework/src/CrestCreates.Web/CrestCreates.Web.csproj`, remove:

```xml
<ItemGroup>
  <Compile Remove="Middlewares\ExceptionHandlingMiddleware.cs" />
</ItemGroup>
```

If the `ItemGroup` becomes empty, remove the empty `ItemGroup`.

- [ ] **Step 3: Verify only one middleware remains**

Run:

```powershell
rg -n "class ExceptionHandlingMiddleware|class ErrorResponse" framework\src framework\test -g "*.cs"
```

Expected:

```text
framework\src\CrestCreates.AspNetCore\Middlewares\ExceptionHandlingMiddleware.cs:...
```

No `CrestCreates.Web.Middlewares.ExceptionHandlingMiddleware` result should remain. `ErrorResponse` should not remain in the exception middleware path.

- [ ] **Step 4: Build Web project**

Run:

```powershell
dotnet build framework\src\CrestCreates.Web\CrestCreates.Web.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

Run:

```powershell
git add framework\src\CrestCreates.Web\CrestCreates.Web.csproj
git rm framework\src\CrestCreates.Web\Middlewares\ExceptionHandlingMiddleware.cs
git commit -m "chore: remove legacy web exception middleware"
```

---

### Task 9: Update Integration Tests For New Envelope

**Files:**
- Modify: `framework/test/CrestCreates.IntegrationTests/ConcurrencyIntegrationTests.cs`
- Modify: `framework/test/CrestCreates.IntegrationTests/IntegrationTests.cs` if it deserializes old `ErrorResponse`

- [ ] **Step 1: Update concurrency integration response model**

In `ConcurrencyIntegrationTests.cs`, replace local old `ErrorResponse` usage with a test-local model matching the new contract:

```csharp
private sealed class CrestErrorResponseForTest
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? TraceId { get; set; }

    public int StatusCode { get; set; }
}
```

- [ ] **Step 2: Update 409 assertions**

For the concurrency exception middleware integration test, assert:

```csharp
context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
errorResponse!.Code.Should().Be("Crest.Concurrency.Conflict");
errorResponse.StatusCode.Should().Be(StatusCodes.Status409Conflict);
errorResponse.TraceId.Should().Be(context.TraceIdentifier);
```

- [ ] **Step 3: Update 428 assertions**

For the precondition exception middleware integration test, assert:

```csharp
context.Response.StatusCode.Should().Be(428);
errorResponse!.Code.Should().Be("Crest.Concurrency.PreconditionRequired");
errorResponse.StatusCode.Should().Be(428);
errorResponse.TraceId.Should().Be(context.TraceIdentifier);
```

- [ ] **Step 4: Search old numeric error-code assertions**

Run:

```powershell
rg -n "response\.Code\.Should\(\)\.Be\(StatusCodes|ErrorResponse|Code = StatusCodes|Code.Should\\(\\)\\.Be\\([0-9]" framework\test -g "*.cs"
```

Expected: no remaining tests assert exception error `Code` is an HTTP number. Dynamic API success `Code = StatusCodes.Status200OK` can remain because that is a separate success envelope.

- [ ] **Step 5: Run integration-focused tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~ConcurrencyIntegrationTests|FullyQualifiedName~TenantBoundary|FullyQualifiedName~IntegrationTests"
```

Expected: relevant integration tests pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add framework\test\CrestCreates.IntegrationTests\ConcurrencyIntegrationTests.cs framework\test\CrestCreates.IntegrationTests\IntegrationTests.cs
git commit -m "test: update integration assertions for error envelope"
```

---

### Task 10: Full Verification

**Files:**
- All files touched by previous tasks

- [ ] **Step 1: Search for forbidden patterns**

Run:

```powershell
rg -n "message\\.Contains|Message\\.Contains|class ErrorResponse|CrestCreates\\.Web\\.Middlewares\\.ExceptionHandlingMiddleware|Compile Remove=\"Middlewares\\ExceptionHandlingMiddleware.cs\"" framework\src framework\test -g "*.cs" -g "*.csproj"
```

Expected: no results for legacy middleware, message-based exception mapping, or old `ErrorResponse` in exception handling.

- [ ] **Step 2: Build main projects**

Run:

```powershell
dotnet build framework\src\CrestCreates.Domain.Shared\CrestCreates.Domain.Shared.csproj --no-restore
dotnet build framework\src\CrestCreates.Domain\CrestCreates.Domain.csproj --no-restore
dotnet build framework\src\CrestCreates.Authorization.Abstractions\CrestCreates.Authorization.Abstractions.csproj --no-restore
dotnet build framework\src\CrestCreates.AspNetCore\CrestCreates.AspNetCore.csproj --no-restore
dotnet build framework\src\CrestCreates.Application\CrestCreates.Application.csproj --no-restore
dotnet build framework\src\CrestCreates.Web\CrestCreates.Web.csproj --no-restore
```

Expected: all builds succeed.

- [ ] **Step 3: Run focused test suites**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~ExceptionHandlingMiddlewareTests|FullyQualifiedName~DefaultCrestExceptionConverterTests|FullyQualifiedName~GeneratedDynamicApiRuntimeTests"
dotnet test framework\test\CrestCreates.Domain.Tests\CrestCreates.Domain.Tests.csproj --filter "FullyQualifiedName~ConcurrencyException"
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~ConcurrencyIntegrationTests"
```

Expected: all focused tests pass.

- [ ] **Step 4: Run broader safety tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj
```

Expected: tests pass. If known unrelated failures exist, record exact test names and error messages in the implementation handoff.

- [ ] **Step 5: Final commit**

Run:

```powershell
git status --short
git add framework docs
git commit -m "feat: add platform global exception handling"
```

Expected: commit includes only the global exception handling implementation and tests.

---

## Self-Review Checklist

| Spec requirement | Covered by |
|---|---|
| Unified `{ code, message, details, traceId, statusCode }` response | Tasks 4, 5, 9 |
| Stable `code`, HTTP `statusCode` | Tasks 3, 4, 5, 9 |
| Business/validation/permission/auth/concurrency/precondition/not-found/infrastructure mapping | Tasks 1, 2, 3, 4 |
| AspNetCore formal middleware only | Tasks 5, 8, 10 |
| Localization by error code | Tasks 4, 7 |
| App service platform exception passthrough | Task 6 |
| Dynamic API/generated CRUD consistency | Tasks 5, 9, 10 |
| 500 safety | Tasks 3, 4, 5 |
| No message-based mapping | Task 10 |
