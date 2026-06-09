# Phase 6: Exposure Layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the Exposure Layer — `AgentToolDescriptor`, `MCPToolDescriptor`, and `DynamicApiEndpointDescriptor` as projection views of `CapabilityDescriptor`. These bridge the metadata system to HTTP, Agent/LLM, and MCP protocol endpoints. Projections do NOT define their own schema — they inherit Input/Output from Capability.

**Architecture:** Projection descriptors are thin wrappers around `VersionedDescriptorRef<CapabilityDescriptor>`. They add exposure-specific metadata (HTTP method, route, tool description, call mode) without duplicating Capability metadata. Agent and MCP tools share `CrestCreates.Exposure.Abstractions`. DynamicApi adds to the existing `CrestCreates.DynamicApi` project. Source generator discovers projection providers.

**Tech Stack:** .NET 10, C# 13, xUnit + FluentAssertions

---

### Task 0: AgentToolDescriptor + ToolCallMode + MCPToolDescriptor

**Files:**
- Create: `framework/src/CrestCreates.Exposure.Abstractions/CrestCreates.Exposure.Abstractions.csproj`
- Create: `framework/src/CrestCreates.Exposure.Abstractions/ToolCallMode.cs`
- Create: `framework/src/CrestCreates.Exposure.Abstractions/AgentToolDescriptor.cs`
- Create: `framework/src/CrestCreates.Exposure.Abstractions/MCPToolDescriptor.cs`

- [ ] **Step 1: Create CrestCreates.Exposure.Abstractions.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Exposure.Abstractions</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write ToolCallMode.cs**

```csharp
namespace CrestCreates.Exposure.Abstractions;

public enum ToolCallMode
{
    Auto,
    RequiresApproval,
    Disabled
}
```

- [ ] **Step 3: Write AgentToolDescriptor.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Exposure.Abstractions;

public sealed class AgentToolDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
    public string Description { get; init; } = string.Empty;
    public ToolCallMode ToolCallMode { get; init; } = ToolCallMode.Auto;
    public int? BudgetLimit { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 4: Write MCPToolDescriptor.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Exposure.Abstractions;

public sealed class MCPToolDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
    public string Description { get; init; } = string.Empty;
    public ToolCallMode ToolCallMode { get; init; } = ToolCallMode.Auto;
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build framework/src/CrestCreates.Exposure.Abstractions/CrestCreates.Exposure.Abstractions.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Exposure.Abstractions/
git commit -m "feat: add Exposure.Abstractions — AgentToolDescriptor, MCPToolDescriptor, ToolCallMode"
```

---

### Task 1: AgentTool Tests

**Files:**
- Create: `framework/test/CrestCreates.Exposure.Tests/CrestCreates.Exposure.Tests.csproj`
- Create: `framework/test/CrestCreates.Exposure.Tests/AgentToolDescriptorTests.cs`
- Create: `framework/test/CrestCreates.Exposure.Tests/MCPToolDescriptorTests.cs`

- [ ] **Step 1: Create CrestCreates.Exposure.Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Exposure.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Exposure.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Exposure.Abstractions\CrestCreates.Exposure.Abstractions.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write AgentToolDescriptorTests.cs (5 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Exposure.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Exposure.Tests;

public class AgentToolDescriptorTests
{
    [Fact]
    public void AgentTool_References_Capability_By_VersionedRef()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 3),
            Description = "Creates a new customer record"
        };

        tool.Capability.Id.Should().Be("cap_01");
        tool.Capability.Version.Should().Be(3);
    }

    [Fact]
    public void AgentTool_Defaults_ToolCallMode_To_Auto()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.ToolCallMode.Should().Be(ToolCallMode.Auto);
    }

    [Fact]
    public void AgentTool_BudgetLimit_Is_Optional()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.BudgetLimit.Should().BeNull();
    }

    [Fact]
    public void AgentTool_Tags_Defaults_To_Empty()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.Tags.Should().BeEmpty();
    }

    [Fact]
    public void AgentTool_Tags_Can_Be_Set()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            Tags = new List<string> { "customer", "crm", "create" }
        };

        tool.Tags.Should().HaveCount(3);
        tool.Tags.Should().Contain("crm");
    }
}
```

- [ ] **Step 3: Write MCPToolDescriptorTests.cs (3 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Exposure.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Exposure.Tests;

public class MCPToolDescriptorTests
{
    [Fact]
    public void MCPTool_References_Capability_By_VersionedRef()
    {
        var tool = new MCPToolDescriptor
        {
            Id = "mcp_01",
            Name = "customer_create",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 2),
            Description = "MCP tool for customer creation"
        };

        tool.Capability.Id.Should().Be("cap_01");
        tool.Capability.Version.Should().Be(2);
    }

    [Fact]
    public void MCPTool_Defaults_ToolCallMode_To_Auto()
    {
        var tool = new MCPToolDescriptor
        {
            Id = "mcp_01",
            Name = "customer_create",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.ToolCallMode.Should().Be(ToolCallMode.Auto);
    }

    [Fact]
    public void MCPTool_Description_Is_Stored()
    {
        var tool = new MCPToolDescriptor
        {
            Id = "mcp_01",
            Name = "customer_create",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            Description = "Creates a customer via MCP protocol"
        };

        tool.Description.Should().Be("Creates a customer via MCP protocol");
    }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Exposure.Tests/CrestCreates.Exposure.Tests.csproj`
Expected: Build succeeded, 8 tests passed.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Exposure.Tests/
git commit -m "feat: add Exposure.Tests — 8 tests for AgentToolDescriptor and MCPToolDescriptor"
```

---

### Task 2: DynamicApi Integration — EndpointDescriptor

**Files:**
- Create: `framework/src/CrestCreates.DynamicApi/CapabilityEndpointDescriptor.cs`
- Modify: `framework/src/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj` (add Capability ref)

The existing `CrestCreates.DynamicApi` project has `DynamicApiServiceDescriptor`, `DynamicApiActionDescriptor`, etc. We add a bridge type that maps a CapabilityDescriptor to an HTTP endpoint.

- [ ] **Step 1: Add Capability.Abstractions reference to DynamicApi.csproj**

Read the existing `framework/src/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj` and add:
```xml
<ProjectReference Include="..\CrestCreates.Capability.Abstractions\CrestCreates.Capability.Abstractions.csproj" />
<ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
```

- [ ] **Step 2: Write CapabilityEndpointDescriptor.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointDescriptor
{
    public VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }
    public HttpMethod HttpMethod { get; init; } = HttpMethod.Post;
    public string RoutePattern { get; init; } = string.Empty;
    public string? GroupName { get; init; }
    public bool RequireAuthorization { get; init; } = true;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public static HttpMethod DeriveHttpMethod(CapabilityKind kind)
        => kind == CapabilityKind.Query ? HttpMethod.Get : HttpMethod.Post;
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build framework/src/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.DynamicApi/
git commit -m "feat: add CapabilityEndpointDescriptor — bridges CapabilityDescriptor to HTTP endpoints"
```

---

### Task 3: DynamicApi + Exposure Integration Tests

**Files:**
- Modify: `framework/test/CrestCreates.Exposure.Tests/CrestCreates.Exposure.Tests.csproj` (add DynamicApi ref)
- Create: `framework/test/CrestCreates.Exposure.Tests/CapabilityEndpointDescriptorTests.cs`

- [ ] **Step 1: Add DynamicApi ref to Exposure.Tests.csproj**

```xml
<ProjectReference Include="..\..\src\CrestCreates.DynamicApi\CrestCreates.DynamicApi.csproj" />
```

- [ ] **Step 2: Write CapabilityEndpointDescriptorTests.cs (4 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Exposure.Tests;

public class CapabilityEndpointDescriptorTests
{
    [Fact]
    public void Endpoint_References_Capability_By_VersionedRef()
    {
        var endpoint = new CapabilityEndpointDescriptor
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            RoutePattern = "/api/customers"
        };

        endpoint.Capability.Id.Should().Be("cap_01");
        endpoint.RoutePattern.Should().Be("/api/customers");
    }

    [Fact]
    public void DeriveHttpMethod_Query_Returns_Get()
    {
        var method = CapabilityEndpointDescriptor.DeriveHttpMethod(CapabilityKind.Query);
        method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public void DeriveHttpMethod_Command_Returns_Post()
    {
        var method = CapabilityEndpointDescriptor.DeriveHttpMethod(CapabilityKind.Command);
        method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public void Endpoint_Defaults_RequireAuthorization_To_True()
    {
        var endpoint = new CapabilityEndpointDescriptor
        {
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            RoutePattern = "/api/test"
        };

        endpoint.RequireAuthorization.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Build and run tests**

Run: `dotnet test framework/test/CrestCreates.Exposure.Tests/CrestCreates.Exposure.Tests.csproj`
Expected: Build succeeded, 12 tests passed (8 previous + 4 new).

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Exposure.Tests/
git commit -m "feat: add CapabilityEndpointDescriptorTests — 4 tests for HTTP endpoint projection"
```

---

### Task 4: slnx Update + Full Build + Final Commit

- [ ] **Step 1: Add Exposure projects to CrestCreates.slnx**

Add in `/src/core/` (alphabetically):
```xml
<Project Path="framework/src/CrestCreates.Exposure.Abstractions/CrestCreates.Exposure.Abstractions.csproj" />
```

Add in `/src/test/` (alphabetically):
```xml
<Project Path="framework/test/CrestCreates.Exposure.Tests/CrestCreates.Exposure.Tests.csproj" />
```

- [ ] **Step 2: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run all tests**

Run: all 8 test projects
Expected: ~142 tests pass (130 previous + 12 new).

- [ ] **Step 4: Final commit**

```bash
git add CrestCreates.slnx
git commit -m "feat: complete Phase 6 — Exposure Layer with 12 tests, slnx updates

- CrestCreates.Exposure.Abstractions: AgentToolDescriptor, MCPToolDescriptor, ToolCallMode
- CapabilityEndpointDescriptor in CrestCreates.DynamicApi (HTTP projection)
- 12 tests: 5 AgentTool + 3 MCPTool + 4 CapabilityEndpoint
- ~142 total tests passing across all phases"
```

---

## Phase 6 Summary

| Component | Project | Tests |
|-----------|---------|-------|
| AgentToolDescriptor | Exposure.Abstractions | 5 |
| MCPToolDescriptor | Exposure.Abstractions | 3 |
| CapabilityEndpointDescriptor | DynamicApi (existing) | 4 |
| **Total** | **1 new project, 1 modified** | **12 new tests** |
