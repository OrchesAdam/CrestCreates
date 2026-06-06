# Generated API Controller Web Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a controller-like authoring model and Web preset facade while keeping the maintained API runtime on source-generated Minimal API endpoints.

**Architecture:** `CrestCreates.DynamicApi` owns generated API descriptors, override semantics, conventions, and lightweight controller helpers. `CrestCreates.CodeGenerator` consumes those compile-time concepts and emits Minimal API mappings. `CrestCreates.Web` exposes ergonomic host presets and generated API configuration without becoming a runtime MVC controller framework.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API endpoint routing, Roslyn incremental source generators, xUnit, FluentAssertions, CrestCreates modularity and Dynamic API.

---

## File Structure

### New Dynamic API Core Files

- `framework/src/CrestCreates.DynamicApi/GeneratedApi/GeneratedApiControllerAttribute.cs`
  - Marks controller-like classes consumed by the generator.
- `framework/src/CrestCreates.DynamicApi/GeneratedApi/ApiOverrideAttribute.cs`
  - Marks a method as replacing a generated CRUD action.
- `framework/src/CrestCreates.DynamicApi/GeneratedApi/CrudAction.cs`
  - Strongly typed CRUD action names.
- `framework/src/CrestCreates.DynamicApi/GeneratedApi/CrestApiController.cs`
  - Lightweight helper base class with no MVC dependency.
- `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointDescriptor.cs`
  - Generated endpoint metadata model.
- `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionContext.cs`
  - Context passed to endpoint conventions.
- `framework/src/CrestCreates.DynamicApi/GeneratedApi/IDynamicApiEndpointConvention.cs`
  - Extension point for generated endpoints.
- `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionRunner.cs`
  - Applies registered conventions after generated mapping.

### Modified Dynamic API Files

- `framework/src/CrestCreates.DynamicApi/DynamicApiOptions.cs`
  - Add convention registration and generated controller compatibility options.
- `framework/src/CrestCreates.DynamicApi/DynamicApiGeneratedRegistryStore.cs`
  - Allow generated providers to expose descriptors and apply conventions.
- `framework/src/CrestCreates.DynamicApi/IDynamicApiGeneratedProvider.cs`
  - Extend the provider contract with descriptors while preserving generated mapping.

### Modified Generator Files

- `framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`
  - Detect `[GeneratedApiController]`.
  - Read route/http metadata at compile time.
  - Emit descriptors.
  - Emit Minimal API mappings for controller-like classes.
  - Apply `ApiOverride(CrudAction.X)` by suppressing matching default CRUD endpoint generation.

### Modified Web Facade Files

- `framework/src/CrestCreates.Web/CrestWebOptions.cs`
  - New options type for Web preset configuration.
- `framework/src/CrestCreates.Web/CrestGeneratedApiWebOptions.cs`
  - New options type for generated API facade configuration.
- `framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs`
  - Add `AddCrestWeb<TModule>()`, options overloads, and `InitializeCrestAsync()`.
  - Keep `UseCrestWeb()` and `MapCrestWeb()` predictable and thin.
- `framework/src/CrestCreates.Web/Controllers/ApiControllerBase.cs`
  - Remove from the recommended surface after new compatibility path is tested. Delete in the final cleanup task if no tests or samples depend on it.

### Modified Sample Files

- `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs`
  - Migrate to the new Web preset while keeping app-specific registrations explicit.
- `samples/SaaSHelpdesk/SaaSHelpdesk.Application/GeneratedApi/TicketApi.cs`
  - Add one controller-like generated API example for custom `GetList`.

### Test Files

- `framework/test/CrestCreates.Web.Tests/DynamicApi/GeneratedApiControllerAbstractionsTests.cs`
- `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/GeneratedApiControllerSourceGeneratorTests.cs`
- `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiEndpointConventionTests.cs`
- `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`
- Existing tests:
  - `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiExtensionsTests.cs`
  - `framework/test/CrestCreates.Web.Tests/DynamicApi/GeneratedDynamicApiRuntimeTests.cs`
  - `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs`

---

## Task 1: Add Generated API Controller Core Abstractions

**Files:**
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/CrudAction.cs`
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/GeneratedApiControllerAttribute.cs`
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/ApiOverrideAttribute.cs`
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/CrestApiController.cs`
- Test: `framework/test/CrestCreates.Web.Tests/DynamicApi/GeneratedApiControllerAbstractionsTests.cs`

- [ ] **Step 1: Write failing abstraction tests**

Create `framework/test/CrestCreates.Web.Tests/DynamicApi/GeneratedApiControllerAbstractionsTests.cs`:

```csharp
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class GeneratedApiControllerAbstractionsTests
{
    [Fact]
    public void CrestApiController_ShouldNotInheritMvcControllerBase()
    {
        typeof(ControllerBase).IsAssignableFrom(typeof(CrestApiController)).Should().BeFalse();
    }

    [Fact]
    public void GeneratedApiControllerAttribute_ShouldStoreRouteTemplate()
    {
        var attribute = new GeneratedApiControllerAttribute("api/books");

        attribute.RouteTemplate.Should().Be("api/books");
    }

    [Fact]
    public void ApiOverrideAttribute_ShouldStoreCrudAction()
    {
        var attribute = new ApiOverrideAttribute(CrudAction.GetList);

        attribute.Action.Should().Be(CrudAction.GetList);
    }

    [Fact]
    public async Task CrestApiController_Ok_ShouldExecuteOkResult()
    {
        var controller = new TestApiController();
        var context = new DefaultHttpContext();

        var result = controller.Ok("created");

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private sealed class TestApiController : CrestApiController
    {
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter GeneratedApiControllerAbstractionsTests
```

Expected: FAIL because `CrestApiController`, `GeneratedApiControllerAttribute`, `ApiOverrideAttribute`, and `CrudAction` do not exist.

- [ ] **Step 3: Add `CrudAction`**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/CrudAction.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public enum CrudAction
{
    Get = 0,
    GetList = 1,
    Create = 2,
    Update = 3,
    Delete = 4
}
```

- [ ] **Step 4: Add `GeneratedApiControllerAttribute`**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/GeneratedApiControllerAttribute.cs`:

```csharp
namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedApiControllerAttribute : Attribute
{
    public GeneratedApiControllerAttribute()
    {
    }

    public GeneratedApiControllerAttribute(string routeTemplate)
    {
        RouteTemplate = routeTemplate;
    }

    public string? RouteTemplate { get; }
}
```

- [ ] **Step 5: Add `ApiOverrideAttribute`**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/ApiOverrideAttribute.cs`:

```csharp
namespace CrestCreates.DynamicApi;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ApiOverrideAttribute : Attribute
{
    public ApiOverrideAttribute(CrudAction action)
    {
        Action = action;
    }

    public CrudAction Action { get; }
}
```

- [ ] **Step 6: Add lightweight `CrestApiController`**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/CrestApiController.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

public abstract class CrestApiController
{
    protected IResult Ok<T>(T value)
    {
        return Results.Ok(value);
    }

    protected IResult NotFound()
    {
        return Results.NotFound();
    }

    protected IResult NoContent()
    {
        return Results.NoContent();
    }
}
```

- [ ] **Step 7: Run abstraction tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter GeneratedApiControllerAbstractionsTests
```

Expected: PASS.

- [ ] **Step 8: Commit**

Run:

```powershell
git add framework/src/CrestCreates.DynamicApi/GeneratedApi framework/test/CrestCreates.Web.Tests/DynamicApi/GeneratedApiControllerAbstractionsTests.cs
git commit -m "feat: add generated api controller abstractions"
```

---

## Task 2: Add Endpoint Descriptor and Convention Pipeline

**Files:**
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointDescriptor.cs`
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionContext.cs`
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/IDynamicApiEndpointConvention.cs`
- Create: `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionRunner.cs`
- Modify: `framework/src/CrestCreates.DynamicApi/DynamicApiOptions.cs`
- Test: `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiEndpointConventionTests.cs`

- [ ] **Step 1: Write failing convention tests**

Create `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiEndpointConventionTests.cs`:

```csharp
using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class DynamicApiEndpointConventionTests
{
    [Fact]
    public void DynamicApiOptions_ShouldRegisterEndpointConvention()
    {
        var options = new DynamicApiOptions();

        options.AddEndpointConvention<TestEndpointConvention>();

        options.EndpointConventionTypes.Should().Contain(typeof(TestEndpointConvention));
    }

    [Fact]
    public void DynamicApiEndpointConventionRunner_ShouldApplyRegisteredConventions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestEndpointConvention>();
        using var provider = services.BuildServiceProvider();

        var descriptor = new DynamicApiEndpointDescriptor(
            "Book",
            "GetList",
            "GET",
            "/api/books",
            typeof(object),
            typeof(object),
            typeof(string),
            Array.Empty<string>(),
            false);

        var builder = new RouteHandlerBuilder(Array.Empty<IEndpointConventionBuilder>());
        var context = new DynamicApiEndpointConventionContext(descriptor, builder);
        var options = new DynamicApiOptions();
        options.AddEndpointConvention<TestEndpointConvention>();

        DynamicApiEndpointConventionRunner.Apply(provider, options, context);

        TestEndpointConvention.AppliedActionName.Should().Be("GetList");
    }

    private sealed class TestEndpointConvention : IDynamicApiEndpointConvention
    {
        public static string? AppliedActionName { get; private set; }

        public void Apply(DynamicApiEndpointConventionContext context)
        {
            AppliedActionName = context.Descriptor.ActionName;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter DynamicApiEndpointConventionTests
```

Expected: FAIL because endpoint descriptor and convention types do not exist.

- [ ] **Step 3: Add descriptor model**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointDescriptor.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public sealed record DynamicApiEndpointDescriptor(
    string ServiceName,
    string ActionName,
    string HttpMethod,
    string RoutePattern,
    Type ServiceType,
    Type? RequestType,
    Type? ResponseType,
    IReadOnlyCollection<string> Permissions,
    bool RequiresTransaction);
```

- [ ] **Step 4: Add convention context**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionContext.cs`:

```csharp
using Microsoft.AspNetCore.Builder;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiEndpointConventionContext
{
    public DynamicApiEndpointConventionContext(
        DynamicApiEndpointDescriptor descriptor,
        RouteHandlerBuilder builder)
    {
        Descriptor = descriptor;
        Builder = builder;
    }

    public DynamicApiEndpointDescriptor Descriptor { get; }

    public RouteHandlerBuilder Builder { get; }
}
```

- [ ] **Step 5: Add convention interface**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/IDynamicApiEndpointConvention.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public interface IDynamicApiEndpointConvention
{
    void Apply(DynamicApiEndpointConventionContext context);
}
```

- [ ] **Step 6: Add convention runner**

Create `framework/src/CrestCreates.DynamicApi/GeneratedApi/DynamicApiEndpointConventionRunner.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DynamicApi;

public static class DynamicApiEndpointConventionRunner
{
    public static void Apply(
        IServiceProvider serviceProvider,
        DynamicApiOptions options,
        DynamicApiEndpointConventionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var conventionType in options.EndpointConventionTypes)
        {
            var convention = (IDynamicApiEndpointConvention)serviceProvider.GetRequiredService(conventionType);
            convention.Apply(context);
        }
    }
}
```

- [ ] **Step 7: Extend `DynamicApiOptions`**

Modify `framework/src/CrestCreates.DynamicApi/DynamicApiOptions.cs` to include:

```csharp
private readonly List<Type> _endpointConventionTypes = new();

public IReadOnlyList<Type> EndpointConventionTypes => _endpointConventionTypes;

public DynamicApiOptions AddEndpointConvention<TConvention>()
    where TConvention : class, IDynamicApiEndpointConvention
{
    var conventionType = typeof(TConvention);
    if (!_endpointConventionTypes.Contains(conventionType))
    {
        _endpointConventionTypes.Add(conventionType);
    }

    return this;
}
```

Keep existing service assembly members unchanged.

- [ ] **Step 8: Run convention tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter DynamicApiEndpointConventionTests
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add framework/src/CrestCreates.DynamicApi framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiEndpointConventionTests.cs
git commit -m "feat: add dynamic api endpoint conventions"
```

---

## Task 3: Generate Endpoint Descriptors for Existing Dynamic API Services

**Files:**
- Modify: `framework/src/CrestCreates.DynamicApi/IDynamicApiGeneratedProvider.cs`
- Modify: `framework/src/CrestCreates.DynamicApi/DynamicApiGeneratedRegistryStore.cs`
- Modify: `framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs`

- [ ] **Step 1: Add failing generator test for descriptors**

In `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs`, add:

```csharp
[Fact]
public async Task DynamicApiAotSourceGenerator_ShouldEmitEndpointDescriptors()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        using System.Threading.Tasks;

        namespace Sample;

        public interface IBookAppService
        {
            Task<string> GetAsync(System.Guid id);
        }

        [CrestService]
        public sealed class BookAppService : IBookAppService
        {
            public Task<string> GetAsync(System.Guid id) => Task.FromResult("book");
        }
        """;

    var result = await SourceGeneratorTestHelper.RunAsync<DynamicApiAotSourceGenerator>(source);

    result.GeneratedSources.Should().ContainKey("GeneratedDynamicApiEndpoints.g.cs");
    result.GeneratedSources["GeneratedDynamicApiEndpoints.g.cs"]
        .Should().Contain("new global::CrestCreates.DynamicApi.DynamicApiEndpointDescriptor(")
        .And.Contain("\"Book\"")
        .And.Contain("\"Get\"");
}
```

- [ ] **Step 2: Run generator test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter DynamicApiAotSourceGenerator_ShouldEmitEndpointDescriptors
```

Expected: FAIL because generated endpoint source does not emit descriptor instances.

- [ ] **Step 3: Extend generated provider contract**

Modify `framework/src/CrestCreates.DynamicApi/IDynamicApiGeneratedProvider.cs`:

```csharp
using Microsoft.AspNetCore.Routing;

namespace CrestCreates.DynamicApi;

public interface IDynamicApiGeneratedProvider
{
    IReadOnlyCollection<System.Reflection.Assembly> ServiceAssemblies { get; }

    IReadOnlyCollection<DynamicApiEndpointDescriptor> EndpointDescriptors { get; }

    DynamicApiRegistry CreateRegistry(DynamicApiOptions options);

    void MapEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options);
}
```

- [ ] **Step 4: Add descriptor aggregation to registry store**

Modify `framework/src/CrestCreates.DynamicApi/DynamicApiGeneratedRegistryStore.cs` by adding:

```csharp
public static IReadOnlyCollection<DynamicApiEndpointDescriptor> GetEndpointDescriptors(DynamicApiOptions options)
{
    ArgumentNullException.ThrowIfNull(options);

    return GetProviders()
        .SelectMany(provider => provider.EndpointDescriptors)
        .Where(descriptor => options.ServiceAssemblies.Count == 0 ||
                             options.ServiceAssemblies.Contains(descriptor.ServiceType.Assembly))
        .ToArray();
}
```

Do not change the existing missing-provider exception behavior.

- [ ] **Step 5: Update generator provider output**

In `DynamicApiAotSourceGenerator.GenerateRegistrySource(...)`, emit an `EndpointDescriptors` property on the generated provider. Use this shape:

```csharp
public global::System.Collections.Generic.IReadOnlyCollection<global::CrestCreates.DynamicApi.DynamicApiEndpointDescriptor> EndpointDescriptors { get; } =
    new global::CrestCreates.DynamicApi.DynamicApiEndpointDescriptor[]
    {
        new global::CrestCreates.DynamicApi.DynamicApiEndpointDescriptor(
            "Book",
            "Get",
            "GET",
            "/api/book/{id}",
            typeof(global::Sample.IBookAppService),
            null,
            typeof(string),
            global::System.Array.Empty<string>(),
            false)
    };
```

Use `ServiceModel.ServiceName`, `ActionModel.ActionName`, resolved HTTP method, resolved route template, service type, request type, response type, permissions, and transaction flag for each generated descriptor.

- [ ] **Step 6: Run generator descriptor test**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter DynamicApiAotSourceGenerator_ShouldEmitEndpointDescriptors
```

Expected: PASS.

- [ ] **Step 7: Run Dynamic API tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter DynamicApi
```

Expected: PASS.

- [ ] **Step 8: Commit**

Run:

```powershell
git add framework/src/CrestCreates.DynamicApi framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs
git commit -m "feat: emit dynamic api endpoint descriptors"
```

---

## Task 4: Apply Endpoint Conventions During Generated Mapping

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs`

- [ ] **Step 1: Add failing generator test for convention runner calls**

In `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs`, add:

```csharp
[Fact]
public async Task DynamicApiAotSourceGenerator_ShouldApplyEndpointConventionsAfterMapping()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        using System.Threading.Tasks;

        namespace Sample;

        public interface IBookAppService
        {
            Task<string> GetListAsync();
        }

        [CrestService]
        public sealed class BookAppService : IBookAppService
        {
            public Task<string> GetListAsync() => Task.FromResult("books");
        }
        """;

    var result = await SourceGeneratorTestHelper.RunAsync<DynamicApiAotSourceGenerator>(source);

    result.GeneratedSources["GeneratedDynamicApiEndpoints.g.cs"]
        .Should().Contain("global::CrestCreates.DynamicApi.DynamicApiEndpointConventionRunner.Apply(")
        .And.Contain("new global::CrestCreates.DynamicApi.DynamicApiEndpointConventionContext(");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter DynamicApiAotSourceGenerator_ShouldApplyEndpointConventionsAfterMapping
```

Expected: FAIL because generated mapping does not invoke the convention runner.

- [ ] **Step 3: Update generated endpoint mapping output**

Modify generator output so each generated endpoint stores its builder:

```csharp
var builder = endpoints.MapMethods(route, methods, handler)
    .WithDisplayName(displayName)
    .WithTags(tags);
```

After metadata is applied, emit:

```csharp
global::CrestCreates.DynamicApi.DynamicApiEndpointConventionRunner.Apply(
    endpoints.ServiceProvider,
    options,
    new global::CrestCreates.DynamicApi.DynamicApiEndpointConventionContext(
        descriptor,
        builder));
```

The `descriptor` argument must be the generated descriptor for the same action.

- [ ] **Step 4: Run convention generator test**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter DynamicApiAotSourceGenerator_ShouldApplyEndpointConventionsAfterMapping
```

Expected: PASS.

- [ ] **Step 5: Run all Dynamic API generator tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter DynamicApi
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs
git commit -m "feat: apply generated api endpoint conventions"
```

---

## Task 5: Add Controller-Like Source Generation

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/GeneratedApiControllerSourceGeneratorTests.cs`

- [ ] **Step 1: Write failing test for controller-like endpoint generation**

Create `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/GeneratedApiControllerSourceGeneratorTests.cs`:

```csharp
using CrestCreates.CodeGenerator.DynamicApiGenerator;
using CrestCreates.CodeGenerator.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.CodeGenerator.Tests.DynamicApiGenerator;

public class GeneratedApiControllerSourceGeneratorTests
{
    [Fact]
    public async Task ShouldGenerateMinimalApiEndpointForGeneratedApiControllerMethod()
    {
        var source = """
            using CrestCreates.DynamicApi;
            using Microsoft.AspNetCore.Mvc;
            using System.Threading.Tasks;

            namespace Sample;

            [GeneratedApiController("api/books")]
            public partial class BookApi : CrestApiController
            {
                [HttpGet("by-slug/{slug}")]
                public Task<string> GetBySlugAsync(string slug)
                {
                    return Task.FromResult(slug);
                }
            }
            """;

        var result = await SourceGeneratorTestHelper.RunAsync<DynamicApiAotSourceGenerator>(source);

        result.GeneratedSources.Should().ContainKey("GeneratedDynamicApiEndpoints.g.cs");
        result.GeneratedSources["GeneratedDynamicApiEndpoints.g.cs"]
            .Should().Contain("MapMethods")
            .And.Contain("api/books/by-slug/{slug}")
            .And.Contain("GetBySlug");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter ShouldGenerateMinimalApiEndpointForGeneratedApiControllerMethod
```

Expected: FAIL because `[GeneratedApiController]` is not discovered by the generator.

- [ ] **Step 3: Add generator model for controller-like classes**

In `DynamicApiAotSourceGenerator.cs`, add a `GeneratedApiControllerModel` record:

```csharp
private sealed record GeneratedApiControllerModel(
    string ControllerName,
    string RouteTemplate,
    string ControllerType,
    ImmutableArray<ActionModel> Actions);
```

Add it to `GenerationContext`:

```csharp
private sealed record GenerationContext(
    string AssemblyName,
    ImmutableArray<ServiceModel> Services,
    ImmutableArray<GeneratedApiControllerModel> Controllers);
```

Update existing construction sites to pass `ImmutableArray<GeneratedApiControllerModel>.Empty` before implementing discovery.

- [ ] **Step 4: Discover `[GeneratedApiController]` classes**

In `BuildGenerationContext`, resolve the attribute:

```csharp
var generatedApiControllerAttribute = compilation.GetTypeByMetadataName("CrestCreates.DynamicApi.GeneratedApiControllerAttribute");
```

Build controller models from public, non-abstract classes that have the attribute:

```csharp
var controllers = generatedApiControllerAttribute is null
    ? ImmutableArray<GeneratedApiControllerModel>.Empty
    : EnumerateNamedTypes(compilation.Assembly)
        .Concat(compilation.SourceModule.ReferencedAssemblySymbols.SelectMany(EnumerateNamedTypes))
        .Where(type => type.TypeKind == TypeKind.Class &&
                       !type.IsAbstract &&
                       type.DeclaredAccessibility == Accessibility.Public &&
                       HasAttribute(type, generatedApiControllerAttribute))
        .Select(type => BuildGeneratedApiControllerModel(type, generatedApiControllerAttribute, dynamicApiIgnoreAttribute, unitOfWorkAttribute))
        .Where(model => model.Actions.Length > 0)
        .ToImmutableArray();
```

- [ ] **Step 5: Resolve controller routes and method routes**

Add helper methods:

```csharp
private static GeneratedApiControllerModel BuildGeneratedApiControllerModel(
    INamedTypeSymbol controllerType,
    INamedTypeSymbol generatedApiControllerAttribute,
    INamedTypeSymbol? dynamicApiIgnoreAttribute,
    INamedTypeSymbol? unitOfWorkAttribute)
{
    var routeTemplate = ResolveGeneratedApiControllerRoute(controllerType, generatedApiControllerAttribute);
    var controllerName = TrimControllerName(controllerType.Name);
    var actions = controllerType.GetMembers()
        .OfType<IMethodSymbol>()
        .Where(method => method.MethodKind == MethodKind.Ordinary &&
                         method.DeclaredAccessibility == Accessibility.Public &&
                         !method.IsStatic &&
                         (dynamicApiIgnoreAttribute is null || !HasAttribute(method, dynamicApiIgnoreAttribute)))
        .Select(method => BuildGeneratedApiControllerAction(method, controllerName, routeTemplate, unitOfWorkAttribute))
        .Where(action => action is not null)
        .Cast<ActionModel>()
        .ToImmutableArray();

    return new GeneratedApiControllerModel(
        controllerName,
        routeTemplate,
        controllerType.ToDisplayString(FullyQualifiedFormat),
        actions);
}
```

Use `GeneratedApiControllerAttribute.RouteTemplate` first. If empty, fallback to `api/{trimmed-controller-name-kebab}`.

- [ ] **Step 6: Emit controller mappings**

Extend `GenerateEndpointsSource` so generated controller models emit Minimal API endpoints in the same provider as service endpoints. The handler should resolve the controller class from DI:

```csharp
var controller = httpContext.RequestServices.GetRequiredService<global::Sample.BookApi>();
var result = await controller.GetBySlugAsync(slug);
return global::CrestCreates.DynamicApi.DynamicApiGeneratedRuntime.WrapResult(result);
```

For methods returning `IResult`, return the result directly.

- [ ] **Step 7: Register generated controller types**

In generated registry service registration output, add scoped registrations:

```csharp
services.TryAddScoped<global::Sample.BookApi>();
```

Use `Microsoft.Extensions.DependencyInjection.Extensions` in generated source if `TryAddScoped` is emitted.

- [ ] **Step 8: Run controller generation test**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter GeneratedApiControllerSourceGeneratorTests
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/GeneratedApiControllerSourceGeneratorTests.cs
git commit -m "feat: generate minimal api from controller-like classes"
```

---

## Task 6: Support CRUD Override Semantics

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs`
- Test: `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/GeneratedApiControllerSourceGeneratorTests.cs`

- [ ] **Step 1: Write failing override test**

Add to `GeneratedApiControllerSourceGeneratorTests.cs`:

```csharp
[Fact]
public async Task ShouldUseApiOverrideToReplaceDefaultGetListEndpoint()
{
    var source = """
        using CrestCreates.Domain.Shared.Attributes;
        using CrestCreates.DynamicApi;
        using System.Threading.Tasks;

        namespace Sample;

        public interface IBookAppService
        {
            Task<string> GetListAsync();
        }

        [CrestService]
        public sealed class BookAppService : IBookAppService
        {
            public Task<string> GetListAsync() => Task.FromResult("default");
        }

        [GeneratedApiController("api/book")]
        public partial class BookApi : CrestApiController
        {
            [ApiOverride(CrudAction.GetList)]
            public Task<string> GetListAsync()
            {
                return Task.FromResult("custom");
            }
        }
        """;

    var result = await SourceGeneratorTestHelper.RunAsync<DynamicApiAotSourceGenerator>(source);
    var generated = result.GeneratedSources["GeneratedDynamicApiEndpoints.g.cs"];

    generated.Should().Contain("BookApi");
    generated.Should().Contain("GetListAsync()");
    generated.Should().NotContain("BookAppService>().GetListAsync()");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter ShouldUseApiOverrideToReplaceDefaultGetListEndpoint
```

Expected: FAIL because default service endpoint generation is not suppressed.

- [ ] **Step 3: Read override metadata in controller actions**

In `BuildGeneratedApiControllerAction`, detect `ApiOverrideAttribute` and store the action in `ActionModel`. Add a nullable property:

```csharp
CrudAction? OverrideAction
```

to the internal action model used for generated controller methods.

- [ ] **Step 4: Suppress matching default CRUD action**

Before service endpoint emission, build a set of overrides keyed by route and action:

```csharp
var overriddenActions = controllers
    .SelectMany(controller => controller.Actions.Select(action => new
    {
        controller.RouteTemplate,
        action.OverrideAction
    }))
    .Where(item => item.OverrideAction is not null)
    .ToHashSet();
```

When emitting service actions, skip an action when:

- controller route equals service route;
- override action equals the service action's CRUD action.

Do not skip custom non-CRUD service methods.

- [ ] **Step 5: Run override test**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter ShouldUseApiOverrideToReplaceDefaultGetListEndpoint
```

Expected: PASS.

- [ ] **Step 6: Run all Dynamic API generator tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter DynamicApi
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add framework/tools/CrestCreates.CodeGenerator/DynamicApiGenerator/DynamicApiAotSourceGenerator.cs framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/GeneratedApiControllerSourceGeneratorTests.cs
git commit -m "feat: support generated api crud overrides"
```

---

## Task 7: Add CrestCreates.Web Preset Options

**Files:**
- Create: `framework/src/CrestCreates.Web/CrestWebOptions.cs`
- Create: `framework/src/CrestCreates.Web/CrestGeneratedApiWebOptions.cs`
- Modify: `framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs`
- Test: `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`

- [ ] **Step 1: Write failing Web preset tests**

Create `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`:

```csharp
using CrestCreates.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests;

public class CrestWebPresetTests
{
    [Fact]
    public void CrestWebOptions_ShouldConfigureGeneratedApiAssemblies()
    {
        var options = new CrestWebOptions();

        options.UseGeneratedApi(api => api.AddApplicationServiceAssembly<CrestWebPresetTests>());

        options.GeneratedApi.ServiceMarkerTypes.Should().Contain(typeof(CrestWebPresetTests));
    }

    [Fact]
    public void AddCrestWeb_ShouldAcceptOptionsDelegate()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCrestWeb(options =>
        {
            options.UseGeneratedApi(api => api.AddApplicationServiceAssembly<CrestWebPresetTests>());
        });

        builder.Services.Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter CrestWebPresetTests
```

Expected: FAIL because `CrestWebOptions` and options overloads do not exist.

- [ ] **Step 3: Add `CrestGeneratedApiWebOptions`**

Create `framework/src/CrestCreates.Web/CrestGeneratedApiWebOptions.cs`:

```csharp
namespace CrestCreates.Web;

public sealed class CrestGeneratedApiWebOptions
{
    private readonly List<Type> _serviceMarkerTypes = new();

    public IReadOnlyList<Type> ServiceMarkerTypes => _serviceMarkerTypes;

    public CrestGeneratedApiWebOptions AddApplicationServiceAssembly<TMarker>()
    {
        var markerType = typeof(TMarker);
        if (!_serviceMarkerTypes.Contains(markerType))
        {
            _serviceMarkerTypes.Add(markerType);
        }

        return this;
    }
}
```

- [ ] **Step 4: Add `CrestWebOptions`**

Create `framework/src/CrestCreates.Web/CrestWebOptions.cs`:

```csharp
namespace CrestCreates.Web;

public sealed class CrestWebOptions
{
    public CrestGeneratedApiWebOptions GeneratedApi { get; } = new();

    public bool EnableOpenIddict { get; private set; } = true;

    public CrestWebOptions UseGeneratedApi(Action<CrestGeneratedApiWebOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(GeneratedApi);
        return this;
    }

    public CrestWebOptions UseOpenIddict(bool enabled = true)
    {
        EnableOpenIddict = enabled;
        return this;
    }
}
```

- [ ] **Step 5: Add `AddCrestWeb` options overload**

Modify `CrestCreatesWebApplicationExtensions.cs`:

```csharp
public static WebApplicationBuilder AddCrestWeb(
    this WebApplicationBuilder builder,
    Action<CrestWebOptions>? configure)
{
    var options = new CrestWebOptions();
    configure?.Invoke(options);
    return AddCrestWeb(builder, options);
}

private static WebApplicationBuilder AddCrestWeb(
    WebApplicationBuilder builder,
    CrestWebOptions options)
{
    builder.Host.UseCrestSerilog();
    builder.Host.UsePinnedScopeServiceProvider();

    var services = builder.Services;
    var configuration = builder.Configuration;

    // Keep existing AddCrestWeb registrations here.
    // Replace the Dynamic API registration block with the configured marker list.
    services.AddCrestAspNetCoreDynamicApi(dynamicApi =>
    {
        foreach (var markerType in options.GeneratedApi.ServiceMarkerTypes)
        {
            dynamicApi.AddApplicationServiceAssembly(markerType.Assembly);
        }
    });

    return builder;
}
```

Keep the existing parameterless `AddCrestWeb()` and make it call `AddCrestWeb(builder, configure: null)`.

- [ ] **Step 6: Run Web preset tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter CrestWebPresetTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add framework/src/CrestCreates.Web framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs
git commit -m "feat: add crest web preset options"
```

---

## Task 8: Add `InitializeCrestAsync` Web Lifecycle Helper

**Files:**
- Modify: `framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs`
- Test: `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`

- [ ] **Step 1: Add failing lifecycle helper test**

Add to `CrestWebPresetTests.cs`:

```csharp
[Fact]
public void InitializeCrestAsync_ShouldBeExposedOnWebApplication()
{
    var method = typeof(CrestCreatesWebApplicationExtensions)
        .GetMethods()
        .SingleOrDefault(method => method.Name == "InitializeCrestAsync");

    method.Should().NotBeNull();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter InitializeCrestAsync_ShouldBeExposedOnWebApplication
```

Expected: FAIL because the extension method does not exist.

- [ ] **Step 3: Add lifecycle helper**

Add to `CrestCreatesWebApplicationExtensions.cs`:

```csharp
public static async Task<WebApplication> InitializeCrestAsync(this WebApplication app)
{
    ArgumentNullException.ThrowIfNull(app);

    await app.InitializeModulesAsync();
    return app;
}
```

Add `using System.Threading.Tasks;` if needed.

- [ ] **Step 4: Run lifecycle helper test**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj --filter InitializeCrestAsync_ShouldBeExposedOnWebApplication
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs
git commit -m "feat: add crest web initialization helper"
```

---

## Task 9: Migrate SaaSHelpdesk.Web to the Web Preset

**Files:**
- Modify: `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs`
- Create: `samples/SaaSHelpdesk/SaaSHelpdesk.Application/GeneratedApi/TicketApi.cs`
- Test: existing `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/SaaSHelpdesk.Tests.csproj`

- [ ] **Step 1: Add a controller-like generated API example**

Create `samples/SaaSHelpdesk/SaaSHelpdesk.Application/GeneratedApi/TicketApi.cs`:

```csharp
using CrestCreates.DynamicApi;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;

namespace SaaSHelpdesk.Application.GeneratedApi;

[GeneratedApiController("api/ticket")]
public partial class TicketApi : CrestApiController
{
    private readonly ITicketAppService _ticketAppService;

    public TicketApi(ITicketAppService ticketAppService)
    {
        _ticketAppService = ticketAppService;
    }

    [ApiOverride(CrudAction.GetList)]
    public Task<IReadOnlyList<TicketDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _ticketAppService.GetAllAsync(cancellationToken);
    }
}
```

This matches the current `ITicketAppService.GetAllAsync(CancellationToken)` signature while replacing the list-style generated endpoint.

- [ ] **Step 2: Run SaaSHelpdesk tests before Program.cs migration**

Run:

```powershell
dotnet test samples/SaaSHelpdesk/SaaSHelpdesk.Tests/SaaSHelpdesk.Tests.csproj --filter TicketApiTests
```

Expected: PASS. If this fails due to existing environment requirements, capture the exact failure and continue only after deciding whether it is unrelated to this migration.

- [ ] **Step 3: Replace framework-default registration blocks in Program.cs**

Modify `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs` to keep this shape:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddCrestWeb(options =>
{
    options.UseGeneratedApi(api =>
    {
        api.AddApplicationServiceAssembly<TicketAppService>();
        api.AddApplicationServiceAssembly<CustomerAppService>();
        api.AddApplicationServiceAssembly<CategoryAppService>();
        api.AddApplicationServiceAssembly<KnowledgeBaseAppService>();
        api.AddApplicationServiceAssembly<SLAPolicyAppService>();
        api.AddApplicationServiceAssembly<DashboardAppService>();
        api.AddApplicationServiceAssembly<AgentAppService>();
        api.AddApplicationServiceAssembly<CustomerPortalAppService>();
        api.AddApplicationServiceAssembly<SettingAppService>();
        api.AddApplicationServiceAssembly<FeatureAppService>();
        api.AddApplicationServiceAssembly<AuditLogAppService>();
        api.AddApplicationServiceAssembly<AuditLogCleanupAppService>();
    });
});

builder.Services.AddAuthentication()
    .AddScheme<SaaSHelpdesk.Web.Auth.CustomerApiKeyOptions, SaaSHelpdesk.Web.Auth.CustomerApiKeyAuthenticationHandler>(
        "CustomerApiKey", options => { });

builder.Services.AddSettingDefinitionProvider<SaaSHelpdesk.Domain.Settings.HelpdeskSettingDefinitionProvider>();
builder.Services.AddValidatorsFromAssemblyContaining<SaaSHelpdesk.Application.Validators.CreateTicketDtoValidator>();
new SecurityModule(builder.Configuration).OnConfigureServices(builder.Services);

builder.Host.RegisterModules();

var app = builder.Build();

app.UseCrestWeb();
app.MapCrestWeb();

await app.InitializeCrestAsync();

app.Run();
```

Keep application-specific using directives. Remove using directives that are only needed by deleted framework-default registration blocks.

- [ ] **Step 4: Run SaaSHelpdesk tests**

Run:

```powershell
dotnet test samples/SaaSHelpdesk/SaaSHelpdesk.Tests/SaaSHelpdesk.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs samples/SaaSHelpdesk/SaaSHelpdesk.Application/GeneratedApi/TicketApi.cs
git commit -m "refactor: migrate saas helpdesk to crest web preset"
```

---

## Task 10: Remove CrestCreates.Web MVC Controller Surface

**Files:**
- Delete: `framework/src/CrestCreates.Web/Controllers/ApiControllerBase.cs`
- Modify tests that reference `ApiControllerBase` only if they are asserting the old Web surface.
- Test: `framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj`

- [ ] **Step 1: Find remaining references**

Run:

```powershell
rg "ApiControllerBase|CrudControllerBase|ControllerBase" framework/src/CrestCreates.Web framework/test samples -n
```

Expected: references to `framework/src/CrestCreates.Web/Controllers/ApiControllerBase.cs` and no active sample dependency on it.

- [ ] **Step 2: Delete Web `ApiControllerBase`**

Delete:

```text
framework/src/CrestCreates.Web/Controllers/ApiControllerBase.cs
```

Do not delete `framework/src/CrestCreates.AspNetCore/Controllers/CrudControllerBase.cs` in this task. That cleanup belongs to a later legacy-controller removal pass.

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj
```

Expected: PASS.

- [ ] **Step 4: Run generator tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj --filter DynamicApi
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add -A framework/src/CrestCreates.Web framework/test/CrestCreates.Web.Tests
git commit -m "refactor: remove web mvc api controller surface"
```

---

## Task 11: Mark Legacy MVC Controller Generators

**Files:**
- Modify: `framework/tools/CrestCreates.CodeGenerator/ControllerGenerator/ControllerSourceGenerator.cs`
- Modify: `framework/tools/CrestCreates.CodeGenerator/ControllerGenerator/CrudControllerSourceGenerator.cs`
- Test: existing `framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj`

- [ ] **Step 1: Add explicit legacy comments and obsolete attributes**

Add `[Obsolete]` to both generator classes:

```csharp
[Obsolete("MVC controller generation is legacy. Use GeneratedApiController with source-generated Minimal API endpoints.")]
```

Add this short comment before each class declaration:

```csharp
// Legacy generator retained only during migration to generated Minimal API endpoints.
```

- [ ] **Step 2: Run code generator tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Commit**

Run:

```powershell
git add framework/tools/CrestCreates.CodeGenerator/ControllerGenerator/ControllerSourceGenerator.cs framework/tools/CrestCreates.CodeGenerator/ControllerGenerator/CrudControllerSourceGenerator.cs
git commit -m "refactor: mark mvc controller generators legacy"
```

---

## Task 12: Final Verification

**Files:**
- No source changes unless verification exposes a real defect.

- [ ] **Step 1: Run framework Web tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj
```

Expected: PASS.

- [ ] **Step 2: Run code generator tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Run SaaSHelpdesk tests**

Run:

```powershell
dotnet test samples/SaaSHelpdesk/SaaSHelpdesk.Tests/SaaSHelpdesk.Tests.csproj
```

Expected: PASS.

- [ ] **Step 4: Run full build**

Run:

```powershell
dotnet build
```

Expected: PASS.

- [ ] **Step 5: Inspect working tree**

Run:

```powershell
git status --short
```

Expected: empty output.

If the working tree is not clean, inspect each file and either commit intended changes or revert only changes created by this implementation.

---

## Self-Review

Spec coverage:

- Controller-like authoring model: Tasks 1, 5, 6.
- AoT/source-generated Minimal API mainline: Tasks 3, 4, 5, 6.
- Web preset facade: Tasks 7, 8, 9.
- SaaSHelpdesk host simplification: Task 9.
- Removal of `CrestCreates.Web` MVC surface: Task 10.
- Legacy generator cleanup: Task 11.
- Verification: Task 12.

Scope check:

- This plan is one implementation stream because the Web preset, generated API authoring model, and generator output must work together to produce a usable sample.
- The plan intentionally stages deletion after replacement behavior is tested.

Type consistency:

- Public user-facing types are in the `CrestCreates.DynamicApi` namespace.
- Web preset types are in the `CrestCreates.Web` namespace.
- Generator changes target `DynamicApiAotSourceGenerator`, the current generated endpoint mainline.
