# Localization Platform Completion Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete the localization platform capability by defining the resource chain, fixing thread safety, adding request culture middleware, and creating tests.

**Tech Stack:** .NET 10, existing Localization module, ASP.NET Core localization middleware

---

## File Structure

### New Files to Create

| File | Purpose |
|------|---------|
| `framework/src/CrestCreates.Localization/Abstractions/ILocalizationResourceContributor.cs` | Interface for module resource contribution |
| `framework/src/CrestCreates.Localization/Abstractions/LocalizationResource.cs` | Resource container model |
| `framework/src/CrestCreates.Localization/Abstractions/ILocalizationContext.cs` | Thread-local culture context |
| `framework/src/CrestCreates.Localization/Services/ThreadSafeLocalizationService.cs` | Thread-safe localization service |
| `framework/test/CrestCreates.Localization.Tests/` | Unit tests project |

### Files to Modify

| File | Changes |
|------|---------|
| `LocalizationService.cs` | Fix thread safety with AsyncLocal culture context |
| `LocalizationModule.cs` | Register new services, add middleware |
| `JsonResourceLocalizationProvider.cs` | Support nested JSON paths |
| `Startup.cs` | Add UseRequestLocalization middleware |

---

## Task 1: Define Localization Resource Main Chain

**Files:**
- Create: `framework/src/CrestCreates.Localization/Abstractions/ILocalizationResourceContributor.cs`
- Create: `framework/src/CrestCreates.Localization/Abstractions/LocalizationResource.cs`
- Create: `framework/src/CrestCreates.Localization/Abstractions/ILocalizationContext.cs`
- Modify: `LocalizationService.cs` to use resource contributors

- [ ] **Step 1: Create ILocalizationResourceContributor**

```csharp
namespace CrestCreates.Localization.Abstractions;

public interface ILocalizationResourceContributor
{
    string ResourceName { get; }

    Task<string?> GetStringAsync(string cultureName, string key);

    Task<IEnumerable<string>> GetAllKeysAsync(string cultureName);

    bool HasKey(string cultureName, string key);
}
```

- [ ] **Step 2: Create LocalizationResource**

```csharp
namespace CrestCreates.Localization.Abstractions;

public class LocalizationResource
{
    public required string Name { get; init; }
    public required ILocalizationResourceContributor Contributor { get; init; }
    public int Priority { get; init; }
}
```

- [ ] **Step 3: Create ILocalizationContext (thread-safe)**

```csharp
namespace CrestCreates.Localization.Abstractions;

public interface ILocalizationContext
{
    string CurrentCulture { get; }
    ILocalizationContext? Parent { get; }

    IDisposable Scope(string cultureName);
}
```

- [ ] **Step 4: Update LocalizationService to use contributors**

```csharp
// Modify LocalizationService to:
// 1. Use AsyncLocal for thread-safe culture context
// 2. Chain through resource contributors
// 3. Support fallback chain: specific -> parent -> default
```

- [ ] **Step 5: Commit**

---

## Task 2: Complete Request Culture Access Chain

**Files:**
- Modify: `framework/src/CrestCreates.Localization/Modules/LocalizationModule.cs`
- Modify: `framework/src/CrestCreates.Web/Startup.cs`
- Fix: `LocalizationService.ChangeCulture()` thread safety

- [ ] **Step 1: Fix ChangeCulture thread safety**

```csharp
// Before (NOT thread-safe):
CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

// After (thread-safe using AsyncLocal):
private readonly AsyncLocal<string?> _currentCulture = new();

public string CurrentCulture => _currentCulture.Value ?? _defaultCulture;

public IDisposable ChangeCulture(string cultureName)
{
    var parent = _currentCulture.Value;
    _currentCulture.Value = cultureName;
    return new CultureScope(this, parent);
}

private class CultureScope : IDisposable
{
    private readonly ILocalizationService _service;
    private readonly string? _parentCulture;

    public CultureScope(ILocalizationService service, string? parentCulture)
    {
        _service = service;
        _parentCulture = parentCulture;
    }

    public void Dispose() => _currentCulture.Value = _parentCulture;
}
```

- [ ] **Step 2: Add request culture middleware integration**

Add to LocalizationModule:
```csharp
public override void OnApplicationInitialization(ApplicationInitializationContext context)
{
    context.ApplicationBuilder.UseRequestLocalization();
}
```

- [ ] **Step 3: Update Startup.cs to use middleware**

```csharp
// In Configure() method:
app.UseRequestLocalization();
```

- [ ] **Step 4: Commit**

---

## Task 3: Create Localization Tests

**Files:**
- Create: `framework/test/CrestCreates.Localization.Tests/CrestCreates.Localization.Tests.csproj`
- Create: `framework/test/CrestCreates.Localization.Tests/LocalizationServiceTests.cs`
- Create: `framework/test/CrestCreates.Localization.Tests/LocalizationContextTests.cs`

- [ ] **Step 1: Create test project**

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
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Localization\CrestCreates.Localization.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create LocalizationServiceTests**

```csharp
public class LocalizationServiceTests
{
    [Fact]
    public async Task GetString_WithValidKey_ReturnsLocalizedString()
    {
        // Arrange
        var service = CreateService();
        await service.ChangeCulture("en");

        // Act
        var result = await service.GetStringAsync("Welcome");

        // Assert
        result.Should().Be("Welcome");
    }

    [Fact]
    public async Task GetString_WithCultureFallback_ReturnsParentCulture()
    {
        // Arrange
        var service = CreateService();
        await service.ChangeCulture("en-US"); // Not found
        // Falls back to "en"

        // Act
        var result = await service.GetStringAsync("Welcome");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetString_WithMissingKey_ReturnsNull()
    {
        // Arrange
        var service = CreateService();
        await service.ChangeCulture("en");

        // Act
        var result = await service.GetStringAsync("NonExistentKey");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetString_ThreadSafe_SameService_MultipleCultures()
    {
        // Arrange
        var service = CreateService();
        var task1 = Task.Run(async () =>
        {
            using (await service.ChangeCultureAsync("en"))
            {
                var result = await service.GetStringAsync("Welcome");
                result.Should().Be("Welcome");
            }
        });
        var task2 = Task.Run(async () =>
        {
            using (await service.ChangeCultureAsync("zh-CN"))
            {
                var result = await service.GetStringAsync("Welcome");
                result.Should().Be("欢迎");
            }
        });

        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task GetString_WithParameters_FormatsCorrectly()
    {
        // Arrange
        var service = CreateService();
        await service.ChangeCulture("en");

        // Act
        var result = await service.GetStringAsync("Hello {0}", "World");

        // Assert
        result.Should().Be("Hello World");
    }
}
```

- [ ] **Step 3: Create LocalizationContextTests**

```csharp
public class LocalizationContextTests
{
    [Fact]
    public void Scope_ChangesCulture_Temporarily()
    {
        // Arrange
        var context = new LocalizationContext("en");

        // Act
        using (context.Scope("zh-CN"))
        {
            // Assert
            context.CurrentCulture.Should().Be("zh-CN");
        }

        // Assert - should revert
        context.CurrentCulture.Should().Be("en");
    }

    [Fact]
    public void Scope_Nested_RevertsCorrectly()
    {
        // Arrange
        var context = new LocalizationContext("en");

        // Act
        using (context.Scope("zh-CN"))
        {
            context.CurrentCulture.Should().Be("zh-CN");

            using (context.Scope("ja"))
            {
                context.CurrentCulture.Should().Be("ja");
            }

            context.CurrentCulture.Should().Be("zh-CN");
        }

        context.CurrentCulture.Should().Be("en");
    }
}
```

- [ ] **Step 4: Run tests**

- [ ] **Step 5: Commit**

---

## Acceptance Criteria

### Task 1 (Localization Resource Chain)
- [ ] ILocalizationResourceContributor interface defined
- [ ] LocalizationResource model defined
- [ ] ILocalizationContext for thread-safe culture tracking
- [ ] Resource contributor chain works

### Task 2 (Request Culture Chain)
- [ ] ChangeCulture thread-safe using AsyncLocal
- [ ] UseRequestLocalization middleware configured
- [ ] Culture scope reverts correctly on dispose

### Task 3 (Tests)
- [ ] LocalizationService tests pass
- [ ] LocalizationContext tests pass
- [ ] Thread safety tests pass
