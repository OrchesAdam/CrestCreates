# Phase 8b Dynamic API Descriptor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `CapabilityEndpointDescriptor` as Dynamic API projection metadata over `CapabilityDescriptor`, with registry, validation, relationship coverage, topology support, and stable hash coverage.

**Architecture:** Dynamic API owns the endpoint projection descriptor and registry. Metadata abstractions remain the shared graph contract. Capability remains the authoritative business descriptor; endpoint descriptors only reference capabilities and describe HTTP exposure metadata. No route binding, handler execution, MVC/controller generation, gateway behavior, or runtime fallback is added.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, existing `RegistryBase<TDescriptor>`, existing descriptor relationship/topology APIs, existing canonical hash source-generator profile system.

## Global Constraints

- `CapabilityEndpointDescriptor` is a projection descriptor, not a business capability authority.
- The endpoint descriptor must not expose endpoint-owned `InputSchema`, `OutputSchema`, `Permissions`, handler, invoker, service method, endpoint delegate, or runtime execution reference.
- `Capability` reference is required where language style allows it; validation must still reject default/empty `VersionedDescriptorRef<CapabilityDescriptor>`.
- `AllowAnonymous` is a projection request, not an authority override; validation rejects it when the referenced capability has permissions or high-risk semantics.
- `RoutePattern` is a normalized external HTTP path and must start with `/`.
- `Projection.OperationId` is contract hash material.
- `CapabilityEndpointRegistry` follows the existing `RegistryBase<TDescriptor>` pattern unless implementation finds a concrete phase-expansion blocker.
- Do not change generated Dynamic API endpoint mapping, `IEndpointRouteBuilder`, MVC controller generation, Swagger UI, `CapabilityDispatcher`, `CapabilityPipeline`, or handler execution.
- Tests must verify metadata/graph behavior only and must not reinforce runtime reflection fallback.

---

## File Structure

Create:

- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj` - descriptor contract assembly shared by DynamicApi implementation and Metadata hashing without creating a project cycle.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointDescriptor.cs` - versioned projection descriptor.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointHttpMethod.cs` - metadata enum for HTTP method.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointAuthorizationMode.cs` - metadata enum for endpoint auth projection.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointParameterSource.cs` - metadata enum for input binding source.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointVisibility.cs` - metadata enum for projection visibility.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointInputBinding.cs` - transport-to-capability input binding model.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointOutputMapping.cs` - output transport mapping model.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointProjectionMetadata.cs` - non-execution projection metadata.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointDescriptorProvider.cs` - generated/manual provider bridge.
- `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointRegistry.cs` - typed endpoint descriptor registry contract.
- `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRegistry.cs` - `RegistryBase<CapabilityEndpointDescriptor>` implementation.
- `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptorValidator.cs` - registry validator for descriptor invariants and capability authority weakening.
- `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRelationshipExtractor.cs` - relationship extractor emitting endpoint-to-capability graph edge.
- `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointDescriptorCanonicalHashProfile.cs` - descriptor hash profile.
- `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointInputBindingCanonicalHashProfile.cs` - nested binding hash profile.
- `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointOutputMappingCanonicalHashProfile.cs` - nested output mapping hash profile.
- `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointProjectionMetadataCanonicalHashProfile.cs` - nested projection hash profile with `OperationId` as contract.
- `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/VersionedDescriptorRefCapabilityCanonicalHashProfile.cs` - exact hash profile for `VersionedDescriptorRef<CapabilityDescriptor>`.
- `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorTests.cs`
- `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRegistryTests.cs`
- `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorValidatorTests.cs`
- `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRelationshipExtractorTests.cs`
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorTopology/CapabilityEndpointTopologyTests.cs`
- `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHash/CapabilityEndpointStableHashTests.cs`

Modify:

- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKind.cs` - add `DynamicApiEndpoint = 7`.
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKindNames.cs` - add canonical string and switch branch.
- `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptor.cs` - move existing POCO to the abstractions project so the implementation project does not own the contract type.
- `src/Framework/Api/CrestCreates.DynamicApi/Modules/DynamicApiModule.cs` - register registry, validation engine, validator, and relationship extractor.
- `src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj` - reference `CrestCreates.DynamicApi.Abstractions` and `CrestCreates.Metadata`.
- `src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj` - reference `CrestCreates.DynamicApi.Abstractions` for canonical hash profiles.
- `tests/Framework/Web/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj` - add direct project references/package references needed for endpoint metadata tests.
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs` - accept `DynamicApiEndpoint` as a valid descriptor kind.
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorKindDenyTests.cs` or adjacent visibility-policy test file - add control-plane kind validity/visibility tests.

---

### Task 1: Descriptor Kind and Endpoint Descriptor Model

**Files:**
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKind.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKindNames.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj`
- Move: `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptor.cs` to `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointDescriptor.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointHttpMethod.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointAuthorizationMode.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointParameterSource.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointVisibility.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointInputBinding.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointOutputMapping.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointProjectionMetadata.cs`
- Modify: `src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj`
- Modify: `tests/Framework/Web/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj`
- Test: `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorTests.cs`

**Interfaces:**
- Produces: `DescriptorKind.DynamicApiEndpoint`
- Produces: `DescriptorKindNames.DynamicApiEndpoint`
- Produces: `CapabilityEndpointDescriptor : IDescriptor, IVersionedDescriptor`
- Produces: `CapabilityEndpointHttpMethod`, `CapabilityEndpointAuthorizationMode`, `CapabilityEndpointParameterSource`, `CapabilityEndpointVisibility`
- Produces: `CapabilityEndpointInputBinding`, `CapabilityEndpointOutputMapping`, `CapabilityEndpointProjectionMetadata`

- [ ] **Step 1: Write failing descriptor model tests**

Create `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorTests.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointDescriptorTests
{
    [Fact]
    public void Descriptor_Implements_VersionedDescriptor()
    {
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "books.create.http",
            Name = "Create Book HTTP Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

        descriptor.Should().BeAssignableTo<IDescriptor>();
        descriptor.Should().BeAssignableTo<IVersionedDescriptor>();
        descriptor.Namespace.Should().Be("dynamic-api-endpoint");
        descriptor.Kind.Should().Be(DescriptorKind.DynamicApiEndpoint);
        descriptor.FullId.Should().Be("dynamic-api-endpoint.books.create.http");
    }

    [Fact]
    public void DescriptorKindNames_Maps_DynamicApiEndpoint()
    {
        DescriptorKindNames.DynamicApiEndpoint.Should().Be("DynamicApiEndpoint");
        DescriptorKindNames.ToCanonicalString(DescriptorKind.DynamicApiEndpoint)
            .Should().Be("DynamicApiEndpoint");
    }

    [Fact]
    public void Descriptor_Does_Not_Expose_Capability_Authority_Fields()
    {
        var properties = typeof(CapabilityEndpointDescriptor)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToArray();

        properties.Should().NotContain("InputSchema");
        properties.Should().NotContain("OutputSchema");
        properties.Should().NotContain("Permissions");
        properties.Should().NotContain("Handler");
        properties.Should().NotContain("Invoker");
        properties.Should().NotContain("ServiceMethod");
        properties.Should().NotContain("EndpointDelegate");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter FullyQualifiedName~CapabilityEndpointDescriptorTests
```

Expected: fail because `DescriptorKind.DynamicApiEndpoint`, supporting enums, and descriptor interface implementation do not exist yet.

- [ ] **Step 3: Add descriptor kind**

Modify `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKind.cs`:

```csharp
namespace CrestCreates.Metadata.Abstractions;

public enum DescriptorKind
{
    Unknown = 0,
    Schema = 1,
    Capability = 2,
    Event = 3,
    Workflow = 4,
    Form = 5,
    HumanTask = 6,
    DynamicApiEndpoint = 7
}
```

Modify `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKindNames.cs` by adding the constant and switch branch:

```csharp
public const string DynamicApiEndpoint = "DynamicApiEndpoint";
```

```csharp
DescriptorKind.DynamicApiEndpoint => DynamicApiEndpoint,
```

- [ ] **Step 4: Add Dynamic API descriptor abstractions project**

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../Metadata/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj" />
    <ProjectReference Include="../../../Runtime/Capability/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Modify `src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj`:

```xml
<ProjectReference Include="..\CrestCreates.DynamicApi.Abstractions\CrestCreates.DynamicApi.Abstractions.csproj" />
```

Modify `tests/Framework/Web/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj`:

```xml
<PackageReference Include="Moq" />
<ProjectReference Include="../../../../src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj" />
<ProjectReference Include="../../../../src/Runtime/Capability/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj" />
<ProjectReference Include="../../../../src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />
```

- [ ] **Step 5: Add endpoint metadata support types**

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointHttpMethod.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public enum CapabilityEndpointHttpMethod
{
    Get,
    Post,
    Put,
    Patch,
    Delete
}
```

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointAuthorizationMode.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public enum CapabilityEndpointAuthorizationMode
{
    InheritCapability,
    RequireAuthenticated,
    AllowAnonymous
}
```

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointParameterSource.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public enum CapabilityEndpointParameterSource
{
    Route,
    Query,
    Header,
    Body
}
```

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointVisibility.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public enum CapabilityEndpointVisibility
{
    Public,
    Internal,
    Hidden
}
```

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointInputBinding.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public sealed record CapabilityEndpointInputBinding
{
    public string Name { get; init; } = string.Empty;
    public CapabilityEndpointParameterSource Source { get; init; }
    public string? CapabilityInputPath { get; init; }
    public bool Required { get; init; } = true;
}
```

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointOutputMapping.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public sealed record CapabilityEndpointOutputMapping
{
    public int SuccessStatusCode { get; init; } = 200;
    public string? ContentType { get; init; }
}
```

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointProjectionMetadata.cs`:

```csharp
namespace CrestCreates.DynamicApi;

public sealed record CapabilityEndpointProjectionMetadata
{
    public string? OperationId { get; init; }
    public string? GroupName { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public bool Deprecated { get; init; }
    public CapabilityEndpointVisibility Visibility { get; init; } = CapabilityEndpointVisibility.Public;
}
```

- [ ] **Step 6: Move and refine CapabilityEndpointDescriptor**

Move the existing descriptor file, then replace its contents:

```bash
git mv src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptor.cs \
       src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointDescriptor.cs
```

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Projection metadata describing how a CapabilityDescriptor is exposed through Dynamic API.
/// This descriptor never owns capability schemas, permissions, handlers, or execution logic.
/// </summary>
public sealed class CapabilityEndpointDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "dynamic-api-endpoint";
    public DescriptorKind Kind => DescriptorKind.DynamicApiEndpoint;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string? SupersededById { get; init; }

    public required VersionedDescriptorRef<CapabilityDescriptor> Capability { get; init; }

    public CapabilityEndpointHttpMethod HttpMethod { get; init; }
    public string RoutePattern { get; init; } = string.Empty;
    public CapabilityEndpointAuthorizationMode AuthorizationMode { get; init; }
        = CapabilityEndpointAuthorizationMode.InheritCapability;

    public IReadOnlyList<CapabilityEndpointInputBinding> InputBindings { get; init; }
        = Array.Empty<CapabilityEndpointInputBinding>();

    public CapabilityEndpointOutputMapping OutputMapping { get; init; } = new();

    public CapabilityEndpointProjectionMetadata Projection { get; init; } = new();
}
```

Remove `DeriveHttpMethod`; it converted capability kind into endpoint method on the descriptor type and is not part of the descriptor authority.

- [ ] **Step 7: Run descriptor model tests**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter FullyQualifiedName~CapabilityEndpointDescriptorTests
```

Expected: pass.

- [ ] **Step 8: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKind.cs \
        src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorKindNames.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointDescriptor.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointHttpMethod.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointAuthorizationMode.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointParameterSource.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointVisibility.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointInputBinding.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointOutputMapping.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CapabilityEndpointProjectionMetadata.cs \
        src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj \
        tests/Framework/Web/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj \
        tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorTests.cs
git commit -m "feat: add dynamic api endpoint descriptor model"
```

### Task 2: Endpoint Descriptor Registry and Validation

**Files:**
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointDescriptorProvider.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointRegistry.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRegistry.cs`
- Create: `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptorValidator.cs`
- Modify: `src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj`
- Test: `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRegistryTests.cs`
- Test: `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorValidatorTests.cs`

**Interfaces:**
- Consumes: `CapabilityEndpointDescriptor`
- Consumes: `ICapabilityRegistry.GetByVersion(string id, int version)`
- Produces: `ICapabilityEndpointDescriptorProvider : IDescriptorProvider<CapabilityEndpointDescriptor>`
- Produces: `ICapabilityEndpointRegistry : IVersionedDescriptorRegistry<CapabilityEndpointDescriptor>`
- Produces: `CapabilityEndpointRegistry`
- Produces: `CapabilityEndpointDescriptorValidator : IRegistryValidator<CapabilityEndpointDescriptor>`

- [ ] **Step 1: Add DynamicApi implementation reference required by registry and validator**

Modify `src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj` to add:

```xml
<ProjectReference Include="../../../Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj" />
```

This gives DynamicApi implementation access to `RegistryBase<TDescriptor>`, `RegistryValidationEngine<TDescriptor>`, and `ICapabilityRegistry`. This does not create a cycle because the descriptor contract lives in `CrestCreates.DynamicApi.Abstractions`, not in the DynamicApi implementation project.

- [ ] **Step 2: Write failing registry tests**

Create `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRegistryTests.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointRegistryTests
{
    [Fact]
    public void Build_Indexes_By_Id_Name_Version_And_Capability()
    {
        var registry = new CapabilityEndpointRegistry(
            new RegistryValidationEngine<CapabilityEndpointDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityEndpointDescriptor>>()));

        var descriptor = CreateEndpoint("books.create.http", "books.create", 1);

        registry.Build(new[] { new TestProvider(descriptor) });

        registry.GetById("books.create.http").Should().BeSameAs(descriptor);
        registry.GetByNameAndVersion("Create Book Endpoint", 1).Should().BeSameAs(descriptor);
        registry.GetActiveVersion("Create Book Endpoint").Should().BeSameAs(descriptor);
        registry.GetByCapability("books.create", 1).Should().ContainSingle().Which.Should().BeSameAs(descriptor);
    }

    [Fact]
    public void GetByCapability_Without_Version_Returns_All_Capability_Endpoints()
    {
        var registry = new CapabilityEndpointRegistry(
            new RegistryValidationEngine<CapabilityEndpointDescriptor>(
                Array.Empty<IRegistryValidator<CapabilityEndpointDescriptor>>()));

        var v1 = CreateEndpoint("books.create.v1.http", "books.create", 1);
        var v2 = CreateEndpoint("books.create.v2.http", "books.create", 2);

        registry.Build(new[] { new TestProvider(v1, v2) });

        registry.GetByCapability("books.create").Should().BeEquivalentTo(new[] { v1, v2 });
    }

    private static CapabilityEndpointDescriptor CreateEndpoint(
        string id,
        string capabilityId,
        int capabilityVersion)
        => new()
        {
            Id = id,
            Name = "Create Book Endpoint",
            Version = capabilityVersion,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, capabilityVersion),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

    private sealed class TestProvider : ICapabilityEndpointDescriptorProvider
    {
        private readonly IReadOnlyList<CapabilityEndpointDescriptor> _descriptors;

        public TestProvider(params CapabilityEndpointDescriptor[] descriptors)
        {
            _descriptors = descriptors;
        }

        public IReadOnlyList<CapabilityEndpointDescriptor> GetDescriptors() => _descriptors;
    }
}
```

- [ ] **Step 3: Write failing validation tests**

Create `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorValidatorTests.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointDescriptorValidatorTests
{
    [Fact]
    public void Validate_Default_Capability_Ref_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            capability: default,
            overrideCapability: true);

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("Capability", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Route_Without_Leading_Slash_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(ValidEndpoint(), routePattern: "api/books");

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("RoutePattern", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowAnonymous_With_Permissions_Fails()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "books.create",
            Name = "Create Book",
            Version = 1,
            Permissions = new[] { "Books.Create" }
        };
        var validator = CreateValidator(capability);
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            authorizationMode: CapabilityEndpointAuthorizationMode.AllowAnonymous);

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("AllowAnonymous", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowAnonymous_With_High_Risk_Fails()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "books.delete",
            Name = "Delete Book",
            Version = 1,
            RiskLevel = CapabilityRiskLevel.High
        };
        var validator = CreateValidator(capability);
        var descriptor = CopyEndpoint(
            ValidEndpoint("books.delete"),
            authorizationMode: CapabilityEndpointAuthorizationMode.AllowAnonymous);

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("high-risk", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Two_Body_Bindings_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            inputBindings: new[]
            {
                new CapabilityEndpointInputBinding { Name = "a", Source = CapabilityEndpointParameterSource.Body },
                new CapabilityEndpointInputBinding { Name = "b", Source = CapabilityEndpointParameterSource.Body }
            });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Route_Token_Without_Binding_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(ValidEndpoint(), routePattern: "/api/books/{id}");

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("id", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Route_Binding_Without_Token_Fails()
    {
        var validator = CreateValidator();
        var descriptor = CopyEndpoint(
            ValidEndpoint(),
            inputBindings: new[]
            {
                new CapabilityEndpointInputBinding
                {
                    Name = "id",
                    Source = CapabilityEndpointParameterSource.Route
                }
            });

        var report = validator.Validate(new[] { descriptor });

        report.HasErrors.Should().BeTrue();
        report.Issues.Should().Contain(i => i.Message.Contains("id", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Valid_Endpoint_Passes()
    {
        var validator = CreateValidator();

        var report = validator.Validate(new[] { ValidEndpoint() });

        report.HasErrors.Should().BeFalse();
    }

    private static CapabilityEndpointDescriptor ValidEndpoint(string capabilityId = "books.create")
        => new()
        {
            Id = capabilityId + ".http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>(capabilityId, 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books",
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 200 }
        };

    private static CapabilityEndpointDescriptor CopyEndpoint(
        CapabilityEndpointDescriptor descriptor,
        VersionedDescriptorRef<CapabilityDescriptor> capability = default,
        bool overrideCapability = false,
        string? routePattern = null,
        CapabilityEndpointAuthorizationMode? authorizationMode = null,
        IReadOnlyList<CapabilityEndpointInputBinding>? inputBindings = null)
        => new()
        {
            Id = descriptor.Id,
            Name = descriptor.Name,
            Version = descriptor.Version,
            Capability = overrideCapability ? capability : descriptor.Capability,
            HttpMethod = descriptor.HttpMethod,
            RoutePattern = routePattern ?? descriptor.RoutePattern,
            AuthorizationMode = authorizationMode ?? descriptor.AuthorizationMode,
            InputBindings = inputBindings ?? descriptor.InputBindings,
            OutputMapping = descriptor.OutputMapping,
            Projection = descriptor.Projection
        };

    private static CapabilityEndpointDescriptorValidator CreateValidator(
        CapabilityDescriptor? capability = null)
    {
        capability ??= new CapabilityDescriptor
        {
            Id = "books.create",
            Name = "Create Book",
            Version = 1,
            RiskLevel = CapabilityRiskLevel.Medium
        };

        var registry = new Mock<ICapabilityRegistry>();
        registry
            .Setup(r => r.GetByVersion(capability.Id, capability.Version))
            .Returns(capability);

        return new CapabilityEndpointDescriptorValidator(registry.Object);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter "FullyQualifiedName~CapabilityEndpointRegistryTests|FullyQualifiedName~CapabilityEndpointDescriptorValidatorTests"
```

Expected: fail because registry/provider/validator types do not exist.

- [ ] **Step 5: Add provider and registry contracts**

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointDescriptorProvider.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

public interface ICapabilityEndpointDescriptorProvider
    : IDescriptorProvider<CapabilityEndpointDescriptor>
{
}
```

Create `src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointRegistry.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

public interface ICapabilityEndpointRegistry
    : IVersionedDescriptorRegistry<CapabilityEndpointDescriptor>
{
    IReadOnlyList<CapabilityEndpointDescriptor> GetByCapability(
        string capabilityId,
        int? capabilityVersion = null);
}
```

- [ ] **Step 6: Add registry implementation**

Create `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRegistry.cs`:

```csharp
using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Registry;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointRegistry
    : RegistryBase<CapabilityEndpointDescriptor>, ICapabilityEndpointRegistry
{
    protected override string RegistryNamespace => "dynamic-api-endpoint";

    public CapabilityEndpointRegistry(
        IRegistryValidationEngine<CapabilityEndpointDescriptor> validationEngine)
        : base(validationEngine)
    {
    }

    CapabilityEndpointDescriptor? IDescriptorRegistry<CapabilityEndpointDescriptor>.GetByName(string name)
        => GetByName(name).FirstOrDefault(d => d.State == DescriptorState.Active)
           ?? GetByName(name).FirstOrDefault();

    public CapabilityEndpointDescriptor? GetByNameAndVersion(string name, int version)
        => GetByName(name).FirstOrDefault(d => d.Version == version);

    public IReadOnlyList<CapabilityEndpointDescriptor> GetAllByName(string name)
        => GetByName(name);

    public CapabilityEndpointDescriptor? GetActiveVersion(string name)
        => GetByName(name).Where(d => d.State == DescriptorState.Active).MaxBy(d => d.Version);

    public CapabilityEndpointDescriptor? GetLatestVersion(string name)
        => GetByName(name).MaxBy(d => d.Version);

    public IReadOnlyList<CapabilityEndpointDescriptor> GetDeprecatedVersions(string name)
        => GetByName(name).Where(d => d.State == DescriptorState.Deprecated).ToList();

    public IReadOnlyList<CapabilityEndpointDescriptor> GetByCapability(
        string capabilityId,
        int? capabilityVersion = null)
    {
        return GetAll()
            .Where(d => d.Capability.Id == capabilityId)
            .Where(d => capabilityVersion is null || d.Capability.Version == capabilityVersion.Value)
            .ToList();
    }

    protected override RegistrySnapshot<CapabilityEndpointDescriptor> BuildSnapshot(
        List<CapabilityEndpointDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<CapabilityEndpointDescriptor>(
            byId,
            byName,
            byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
```

- [ ] **Step 7: Add validator implementation**

Create `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptorValidator.cs`:

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointDescriptorValidator
    : IRegistryValidator<CapabilityEndpointDescriptor>
{
    private readonly ICapabilityRegistry? _capabilityRegistry;

    public CapabilityEndpointDescriptorValidator(ICapabilityRegistry? capabilityRegistry = null)
    {
        _capabilityRegistry = capabilityRegistry;
    }

    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<CapabilityEndpointDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var descriptor in descriptors)
        {
            ValidateIdentity(descriptor, issues);
            ValidateRoute(descriptor, issues);
            ValidateBindings(descriptor, issues);
            ValidateOutput(descriptor, issues);
            ValidateCapabilityAuthority(descriptor, issues);
            ValidateProjection(descriptor, issues);
        }

        return new ValidationReport(issues);
    }

    private static void ValidateIdentity(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id))
            AddError(issues, "Capability endpoint Id is required.");
        if (string.IsNullOrWhiteSpace(descriptor.Name))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Name is required.");
        if (descriptor.Version <= 0)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Version must be greater than zero.");
        if (string.IsNullOrWhiteSpace(descriptor.Capability.Id) || descriptor.Capability.Version <= 0)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Capability reference must specify Id and Version.");
    }

    private static void ValidateRoute(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.RoutePattern))
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' RoutePattern is required.");
            return;
        }

        if (!descriptor.RoutePattern.StartsWith("/", StringComparison.Ordinal))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' RoutePattern must start with '/'.");
    }

    private static void ValidateBindings(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        var bodyCount = descriptor.InputBindings.Count(b => b.Source == CapabilityEndpointParameterSource.Body);
        if (bodyCount > 1)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' may have at most one body input binding.");

        var routeTokens = ExtractRouteTokens(descriptor.RoutePattern)
            .ToHashSet(StringComparer.Ordinal);

        var routeBindings = descriptor.InputBindings
            .Where(b => b.Source == CapabilityEndpointParameterSource.Route)
            .Select(b => b.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var token in routeTokens.Except(routeBindings))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' route token '{token}' has no route input binding.");

        foreach (var binding in routeBindings.Except(routeTokens))
            AddError(issues, $"Capability endpoint '{descriptor.Id}' route input binding '{binding}' has no route token.");
    }

    private static void ValidateOutput(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (descriptor.OutputMapping.SuccessStatusCode is < 200 or > 299)
            AddError(issues, $"Capability endpoint '{descriptor.Id}' success status code must be between 200 and 299.");
    }

    private void ValidateCapabilityAuthority(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Capability.Id) || descriptor.Capability.Version <= 0)
            return;

        var capability = _capabilityRegistry?.GetByVersion(descriptor.Capability.Id, descriptor.Capability.Version);
        if (_capabilityRegistry is not null && capability is null)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' references missing Capability '{descriptor.Capability.Id}' v{descriptor.Capability.Version}.");
            return;
        }

        if (descriptor.AuthorizationMode != CapabilityEndpointAuthorizationMode.AllowAnonymous || capability is null)
            return;

        if (capability.Permissions.Count > 0)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' AllowAnonymous would weaken Capability '{capability.Id}' permissions.");
        }

        if (capability.RiskLevel >= CapabilityRiskLevel.High)
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' AllowAnonymous would weaken high-risk Capability '{capability.Id}'.");
        }
    }

    private static void ValidateProjection(
        CapabilityEndpointDescriptor descriptor,
        List<ValidationIssue> issues)
    {
        if (descriptor.Projection.OperationId is not null
            && string.IsNullOrWhiteSpace(descriptor.Projection.OperationId))
        {
            AddError(issues, $"Capability endpoint '{descriptor.Id}' Projection.OperationId must be stable and non-empty when specified.");
        }
    }

    private static void AddError(List<ValidationIssue> issues, string message)
        => issues.Add(new ValidationIssue(SeverityLevel.Error, message));

    private static IEnumerable<string> ExtractRouteTokens(string routePattern)
    {
        var index = 0;
        while (index < routePattern.Length)
        {
            var start = routePattern.IndexOf('{', index);
            if (start < 0)
                yield break;

            var end = routePattern.IndexOf('}', start + 1);
            if (end < 0)
                yield break;

            var token = routePattern[(start + 1)..end];
            var constraintIndex = token.IndexOf(':', StringComparison.Ordinal);
            if (constraintIndex >= 0)
                token = token[..constraintIndex];

            if (!string.IsNullOrWhiteSpace(token))
                yield return token;

            index = end + 1;
        }
    }
}
```

- [ ] **Step 8: Run registry and validation tests**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter "FullyQualifiedName~CapabilityEndpointRegistryTests|FullyQualifiedName~CapabilityEndpointDescriptorValidatorTests"
```

Expected: pass.

- [ ] **Step 9: Commit**

```bash
git add src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointDescriptorProvider.cs \
        src/Framework/Api/CrestCreates.DynamicApi.Abstractions/ICapabilityEndpointRegistry.cs \
        src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRegistry.cs \
        src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointDescriptorValidator.cs \
        tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRegistryTests.cs \
        tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDescriptorValidatorTests.cs
git commit -m "feat: add capability endpoint registry validation"
```

### Task 3: Relationship Extraction and Topology Coverage

**Files:**
- Create: `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRelationshipExtractor.cs`
- Test: `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRelationshipExtractorTests.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorTopology/CapabilityEndpointTopologyTests.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`

**Interfaces:**
- Consumes: `CapabilityEndpointDescriptor`
- Produces: `CapabilityEndpointRelationshipExtractor : DescriptorRelationshipExtractorBase<CapabilityEndpointDescriptor>`
- Produces edge: `dynamic-api-endpoint:{endpointId}@version -> capability:{capabilityId}@version`

- [ ] **Step 1: Add Metadata.Tests reference to DynamicApi**

Modify `tests/Metadata/Core/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`:

```xml
<ProjectReference Include="../../../../src/Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj" />
<ProjectReference Include="../../../../src/Framework/Api/CrestCreates.DynamicApi/CrestCreates.DynamicApi.csproj" />
```

This is for topology tests that build a descriptor inventory containing `CapabilityEndpointDescriptor`.

- [ ] **Step 2: Write failing relationship extractor tests**

Create `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRelationshipExtractorTests.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointRelationshipExtractorTests
{
    [Fact]
    public void Extract_Returns_Strong_Reference_To_Capability()
    {
        var descriptor = new CapabilityEndpointDescriptor
        {
            Id = "books.create.http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 3),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };
        var extractor = new CapabilityEndpointRelationshipExtractor();

        var relationships = extractor.Extract(descriptor);

        var relationship = relationships.Should().ContainSingle().Subject;
        relationship.From.Should().Be(new DescriptorRef("dynamic-api-endpoint", "books.create.http", 1));
        relationship.To.Should().Be(new DescriptorRef("capability", "books.create", 3));
        relationship.Kind.Should().Be(RelationshipKind.References);
        relationship.Role.Should().Be("Capability");
        relationship.SourcePath.Should().Be(nameof(CapabilityEndpointDescriptor.Capability));
        relationship.Strength.Should().Be(RelationshipStrength.Strong);
        relationship.IsRuntimeBinding.Should().BeFalse();
    }

    [Fact]
    public void SupportedKind_Is_DynamicApiEndpoint()
    {
        new CapabilityEndpointRelationshipExtractor()
            .SupportedKind.Should().Be(DescriptorKind.DynamicApiEndpoint);
    }
}
```

- [ ] **Step 3: Write failing topology tests**

Create `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorTopology/CapabilityEndpointTopologyTests.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorRelationship;
using CrestCreates.Metadata.DescriptorTopology;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class CapabilityEndpointTopologyTests
{
    [Fact]
    public void Build_Connects_Endpoint_To_Capability()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "books.create",
            Name = "Create Book",
            Version = 1
        };
        var endpoint = CreateEndpoint();
        var builder = CreateBuilder();

        var snapshot = builder.Build(new IDescriptor[] { capability, endpoint });

        var endpointRef = new DescriptorRef("dynamic-api-endpoint", "books.create.http", 1);
        var capabilityRef = new DescriptorRef("capability", "books.create", 1);
        snapshot.GetDirectDependencies(endpointRef)
            .Should().ContainSingle(n => n.Ref == capabilityRef);
        snapshot.GetDirectDependents(capabilityRef)
            .Should().ContainSingle(n => n.Ref == endpointRef);
    }

    [Fact]
    public void Build_Missing_Capability_Reports_Missing_Target()
    {
        var builder = CreateBuilder();

        var snapshot = builder.Build(new IDescriptor[] { CreateEndpoint() });

        snapshot.Diagnostics.All.Should().Contain(d =>
            d.Code.Value == "MISSING_TARGET"
            && d.Message.Contains("capability.books.create", StringComparison.Ordinal));
    }

    private static CapabilityEndpointDescriptor CreateEndpoint()
        => new()
        {
            Id = "books.create.http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", 1),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = "/api/books"
        };

    private static DescriptorTopologyBuilder CreateBuilder()
    {
        var provider = new DefaultDescriptorRelationshipProvider(new IDescriptorRelationshipExtractor[]
        {
            new CapabilityEndpointRelationshipExtractor()
        });
        var hashComputer = new DefaultCanonicalHashComputer();
        var hashBuilder = new DescriptorStableHashBuilder(hashComputer);
        return new DescriptorTopologyBuilder(provider, hashBuilder);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter FullyQualifiedName~CapabilityEndpointRelationshipExtractorTests
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter FullyQualifiedName~CapabilityEndpointTopologyTests
```

Expected: relationship tests fail because extractor does not exist; topology tests may also fail until hash profiles are added in Task 4. Keep topology tests in place as cross-task acceptance.

- [ ] **Step 5: Add relationship extractor**

Create `src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRelationshipExtractor.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointRelationshipExtractor
    : DescriptorRelationshipExtractorBase<CapabilityEndpointDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.DynamicApiEndpoint;

    protected override IReadOnlyList<DescriptorRelationship> Extract(
        CapabilityEndpointDescriptor descriptor)
    {
        return
        [
            new DescriptorRelationship(
                From: new DescriptorRef(
                    descriptor.Namespace,
                    descriptor.Id,
                    descriptor.Version),
                To: new DescriptorRef(
                    "capability",
                    descriptor.Capability.Id,
                    descriptor.Capability.Version),
                Kind: RelationshipKind.References,
                Role: "Capability",
                SourcePath: nameof(CapabilityEndpointDescriptor.Capability),
                Strength: RelationshipStrength.Strong,
                IsRuntimeBinding: false)
        ];
    }
}
```

- [ ] **Step 6: Run relationship tests**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter FullyQualifiedName~CapabilityEndpointRelationshipExtractorTests
```

Expected: pass.

- [ ] **Step 7: Commit relationship extractor**

```bash
git add src/Framework/Api/CrestCreates.DynamicApi/CapabilityEndpointRelationshipExtractor.cs \
        tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointRelationshipExtractorTests.cs \
        tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorTopology/CapabilityEndpointTopologyTests.cs \
        tests/Metadata/Core/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj
git commit -m "feat: add capability endpoint relationship coverage"
```

### Task 4: Canonical Hash Coverage

**Files:**
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointDescriptorCanonicalHashProfile.cs`
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointInputBindingCanonicalHashProfile.cs`
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointOutputMappingCanonicalHashProfile.cs`
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointProjectionMetadataCanonicalHashProfile.cs`
- Create: `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/VersionedDescriptorRefCapabilityCanonicalHashProfile.cs`
- Modify: `src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHash/CapabilityEndpointStableHashTests.cs`
- Test: `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorTopology/CapabilityEndpointTopologyTests.cs`

**Interfaces:**
- Consumes: `CapabilityEndpointDescriptor`
- Produces: canonical hash support for contract and definition hash
- Makes Task 3 topology tests pass because topology hashes every node

- [ ] **Step 1: Add Metadata project reference to DynamicApi abstractions**

Modify `src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj`:

```xml
<ProjectReference Include="../../Framework/Api/CrestCreates.DynamicApi.Abstractions/CrestCreates.DynamicApi.Abstractions.csproj" />
```

This lets Metadata hash the endpoint descriptor contract without referencing the DynamicApi implementation project. Do not reference `CrestCreates.DynamicApi` from `CrestCreates.Metadata`; that would create a cycle because DynamicApi implementation needs Metadata for `RegistryBase<TDescriptor>`.

- [ ] **Step 2: Write failing stable hash tests**

Create `tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHash/CapabilityEndpointStableHashTests.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorStableHash;

public class CapabilityEndpointStableHashTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();

    [Fact]
    public void RoutePattern_Change_Changes_ContractHash()
    {
        var first = CreateEndpoint(route: "/api/books");
        var second = CreateEndpoint(route: "/api/library-books");

        Hash(first).Should().NotBe(Hash(second));
    }

    [Fact]
    public void Capability_Version_Change_Changes_ContractHash()
    {
        var first = CreateEndpoint(capabilityVersion: 1);
        var second = CreateEndpoint(capabilityVersion: 2);

        Hash(first).Should().NotBe(Hash(second));
    }

    [Fact]
    public void OperationId_Change_Changes_ContractHash()
    {
        var first = CreateEndpoint(operationId: "Books_Create");
        var second = CreateEndpoint(operationId: "Books_Create_V2");

        Hash(first).Should().NotBe(Hash(second));
    }

    [Fact]
    public void Summary_Change_Changes_DefinitionHash_Not_ContractHash()
    {
        var first = CreateEndpoint(summary: "Create a book");
        var second = CreateEndpoint(summary: "Creates one library book");

        _hashComputer.ComputeContractHash(first, CanonicalHashScope.InternalFull).Value
            .Should().Be(_hashComputer.ComputeContractHash(second, CanonicalHashScope.InternalFull).Value);
        _hashComputer.ComputeDefinitionHash(first, CanonicalHashScope.InternalFull).Value
            .Should().NotBe(_hashComputer.ComputeDefinitionHash(second, CanonicalHashScope.InternalFull).Value);
    }

    private string Hash(CapabilityEndpointDescriptor descriptor)
        => _hashComputer.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull).Value;

    private static CapabilityEndpointDescriptor CreateEndpoint(
        string route = "/api/books",
        int capabilityVersion = 1,
        string? operationId = "Books_Create",
        string? summary = null)
        => new()
        {
            Id = "books.create.http",
            Name = "Create Book Endpoint",
            Version = 1,
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("books.create", capabilityVersion),
            HttpMethod = CapabilityEndpointHttpMethod.Post,
            RoutePattern = route,
            AuthorizationMode = CapabilityEndpointAuthorizationMode.InheritCapability,
            InputBindings = new[]
            {
                new CapabilityEndpointInputBinding
                {
                    Name = "body",
                    Source = CapabilityEndpointParameterSource.Body,
                    CapabilityInputPath = "$"
                }
            },
            OutputMapping = new CapabilityEndpointOutputMapping { SuccessStatusCode = 201 },
            Projection = new CapabilityEndpointProjectionMetadata
            {
                OperationId = operationId,
                Summary = summary,
                Tags = new[] { "Books" }
            }
        };
}
```

- [ ] **Step 3: Run stable hash tests to verify they fail**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter FullyQualifiedName~CapabilityEndpointStableHashTests
```

Expected: fail because canonical hash dispatcher has no profile for `CapabilityEndpointDescriptor`.

- [ ] **Step 4: Add nested hash profiles**

Create `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/VersionedDescriptorRefCapabilityCanonicalHashProfile.cs`:

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(VersionedDescriptorRef<CapabilityDescriptor>),
    ContractShapeVersion = "capability-ref-hash-v1",
    DefinitionShapeVersion = "capability-ref-hash-v1")]
internal sealed class VersionedDescriptorRefCapabilityCanonicalHashProfile
{
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.Version), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.SelectionMode), CanonicalHashFieldClassification.Excluded,
        Reason = "Protocol-level resolution concern - not part of structural hash")]
    [CanonicalHashField(nameof(VersionedDescriptorRef<CapabilityDescriptor>.ExpectedContractHash), CanonicalHashFieldClassification.Excluded,
        Reason = "Resolution-time input - not part of structural hash")]
    private static void Fields() { }
}
```

Create `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointInputBindingCanonicalHashProfile.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityEndpointInputBinding),
    ContractShapeVersion = "capability-endpoint-input-binding-hash-v1",
    DefinitionShapeVersion = "capability-endpoint-input-binding-hash-v1")]
internal sealed class CapabilityEndpointInputBindingCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.Name), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.Source), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.CapabilityInputPath), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(CapabilityEndpointInputBinding.Required), CanonicalHashFieldClassification.Contract, Order = 3)]
    private static void Fields() { }
}
```

Create `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointOutputMappingCanonicalHashProfile.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityEndpointOutputMapping),
    ContractShapeVersion = "capability-endpoint-output-mapping-hash-v1",
    DefinitionShapeVersion = "capability-endpoint-output-mapping-hash-v1")]
internal sealed class CapabilityEndpointOutputMappingCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityEndpointOutputMapping.SuccessStatusCode), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointOutputMapping.ContentType), CanonicalHashFieldClassification.Contract, Order = 1)]
    private static void Fields() { }
}
```

Create `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointProjectionMetadataCanonicalHashProfile.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.Unknown,
    TargetType = typeof(CapabilityEndpointProjectionMetadata),
    ContractShapeVersion = "capability-endpoint-projection-hash-v1",
    DefinitionShapeVersion = "capability-endpoint-projection-hash-v1")]
internal sealed class CapabilityEndpointProjectionMetadataCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.OperationId), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.GroupName), CanonicalHashFieldClassification.DefinitionOnly, Order = 10)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Tags), CanonicalHashFieldClassification.DefinitionOnly, Order = 20,
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByValue)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Summary), CanonicalHashFieldClassification.DefinitionOnly, Order = 30)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Description), CanonicalHashFieldClassification.DefinitionOnly, Order = 40)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Deprecated), CanonicalHashFieldClassification.DefinitionOnly, Order = 50)]
    [CanonicalHashField(nameof(CapabilityEndpointProjectionMetadata.Visibility), CanonicalHashFieldClassification.DefinitionOnly, Order = 60)]
    private static void Fields() { }
}
```

- [ ] **Step 5: Add descriptor hash profile**

Create `src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointDescriptorCanonicalHashProfile.cs`:

```csharp
using CrestCreates.DynamicApi;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.CanonicalHashing.Profiles;

[CanonicalHashProfile(
    ArtifactKind = CanonicalHashArtifactKind.Descriptor,
    DescriptorKind = DescriptorKind.DynamicApiEndpoint,
    TargetType = typeof(CapabilityEndpointDescriptor),
    ContractShapeVersion = "dynamic-api-endpoint-contract-hash-v1",
    DefinitionShapeVersion = "dynamic-api-endpoint-definition-hash-v1")]
internal sealed class CapabilityEndpointDescriptorCanonicalHashProfile
{
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Id), CanonicalHashFieldClassification.Contract, Order = 0)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Name), CanonicalHashFieldClassification.Contract, Order = 1)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Version), CanonicalHashFieldClassification.Contract, Order = 2)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.State), CanonicalHashFieldClassification.Contract, Order = 3)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.SupersededById), CanonicalHashFieldClassification.Contract, Order = 4)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Capability), CanonicalHashFieldClassification.Contract, Order = 10,
        ValueProfile = typeof(VersionedDescriptorRefCapabilityCanonicalHashProfile))]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.HttpMethod), CanonicalHashFieldClassification.Contract, Order = 20)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.RoutePattern), CanonicalHashFieldClassification.Contract, Order = 21)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.AuthorizationMode), CanonicalHashFieldClassification.Contract, Order = 22)]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.InputBindings), CanonicalHashFieldClassification.Contract, Order = 30,
        ElementProfile = typeof(CapabilityEndpointInputBindingCanonicalHashProfile),
        CollectionOrderMode = CanonicalHashCollectionOrderMode.OrdinalByProperty,
        OrderByProperty = "Source,Name,CapabilityInputPath")]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.OutputMapping), CanonicalHashFieldClassification.Contract, Order = 40,
        ValueProfile = typeof(CapabilityEndpointOutputMappingCanonicalHashProfile))]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Projection), CanonicalHashFieldClassification.Contract, Order = 50,
        ValueProfile = typeof(CapabilityEndpointProjectionMetadataCanonicalHashProfile))]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Namespace), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant - not part of hash")]
    [CanonicalHashField(nameof(CapabilityEndpointDescriptor.Kind), CanonicalHashFieldClassification.Excluded,
        Reason = "Runtime constant - not part of hash")]
    private static void Fields() { }
}
```

- [ ] **Step 6: Run hash and topology tests**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~CapabilityEndpointStableHashTests|FullyQualifiedName~CapabilityEndpointTopologyTests"
```

Expected: pass.

- [ ] **Step 7: Run dependency boundary tests**

Run:

```bash
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: pass. If this fails because `Metadata` references the DynamicApi abstractions project, stop and revise the architecture with the user before continuing. Do not work around the boundary test by weakening it.

- [ ] **Step 8: Commit**

```bash
git add src/Metadata/CrestCreates.Metadata/CrestCreates.Metadata.csproj \
        src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointDescriptorCanonicalHashProfile.cs \
        src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointInputBindingCanonicalHashProfile.cs \
        src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointOutputMappingCanonicalHashProfile.cs \
        src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/CapabilityEndpointProjectionMetadataCanonicalHashProfile.cs \
        src/Metadata/CrestCreates.Metadata/CanonicalHashing/Profiles/VersionedDescriptorRefCapabilityCanonicalHashProfile.cs \
        tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorStableHash/CapabilityEndpointStableHashTests.cs
git commit -m "feat: add dynamic api endpoint stable hash coverage"
```

### Task 5: Control Plane Descriptor Kind Validity

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs`
- Test: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorKindDenyTests.cs` or a new adjacent file `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DynamicApiEndpointDescriptorKindPolicyTests.cs`

**Interfaces:**
- Consumes: `DescriptorKind.DynamicApiEndpoint`
- Produces: control plane kind validation recognizes the new descriptor kind without broadening closed-world visibility

- [ ] **Step 1: Write failing control-plane tests**

Create `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DynamicApiEndpointDescriptorKindPolicyTests.cs`:

```csharp
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class DynamicApiEndpointDescriptorKindPolicyTests
{
    [Fact]
    public void DynamicApiEndpoint_Is_Valid_Descriptor_Kind()
    {
        AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(DescriptorKind.DynamicApiEndpoint)
            .Should().BeTrue();
    }

    [Fact]
    public void Closed_World_Denies_DynamicApiEndpoint_When_Not_Allowed()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowedDescriptorKinds = new[] { nameof(DescriptorKind.Capability) }
        });

        evaluator.Evaluate(DescriptorKind.DynamicApiEndpoint)
            .Should().Be(AgentDescriptorKindDecision.Denied);
    }

    [Fact]
    public void Deny_Rule_Overrides_Allow_For_DynamicApiEndpoint()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowedDescriptorKinds = new[] { nameof(DescriptorKind.DynamicApiEndpoint) },
            DeniedDescriptorKinds = new[] { nameof(DescriptorKind.DynamicApiEndpoint) }
        });

        evaluator.Evaluate(DescriptorKind.DynamicApiEndpoint)
            .Should().Be(AgentDescriptorKindDecision.Denied);
    }
}
```

If the evaluator is `internal`, add the tests to the existing assembly that already has `InternalsVisibleTo`, or reuse the existing test pattern in `DescriptorKindDenyTests.cs`.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter FullyQualifiedName~DynamicApiEndpointDescriptorKindPolicyTests
```

Expected: fail because `IsValidDescriptorKind` ends at `HumanTask`.

- [ ] **Step 3: Update kind validation**

Modify `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs`:

```csharp
internal static bool IsValidDescriptorKind(DescriptorKind kind)
{
    return kind is DescriptorKind.Schema
        or DescriptorKind.Capability
        or DescriptorKind.Event
        or DescriptorKind.Workflow
        or DescriptorKind.Form
        or DescriptorKind.HumanTask
        or DescriptorKind.DynamicApiEndpoint;
}
```

Prefer explicit enum members over range checks so future descriptor kind insertions do not accidentally broaden visibility.

- [ ] **Step 4: Run control-plane tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~DynamicApiEndpointDescriptorKindPolicyTests|FullyQualifiedName~DescriptorKindDenyTests"
```

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentDescriptorKindPolicyEvaluator.cs \
        tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DynamicApiEndpointDescriptorKindPolicyTests.cs
git commit -m "feat: recognize dynamic api endpoint descriptor kind"
```

### Task 6: DI Registration and Final Verification

**Files:**
- Modify: `src/Framework/Api/CrestCreates.DynamicApi/Modules/DynamicApiModule.cs`
- Test: `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDynamicApiModuleTests.cs`

**Interfaces:**
- Consumes: `CapabilityEndpointRegistry`, `CapabilityEndpointDescriptorValidator`, `CapabilityEndpointRelationshipExtractor`
- Produces: DynamicApi module registers the metadata projection components

- [ ] **Step 1: Write failing DI registration tests**

Create `tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDynamicApiModuleTests.cs`:

```csharp
using System.Linq;
using CrestCreates.DynamicApi;
using CrestCreates.DynamicApi.Modules;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Web.Tests.DynamicApi;

public class CapabilityEndpointDynamicApiModuleTests
{
    [Fact]
    public void OnConfigureServices_Registers_CapabilityEndpoint_Metadata_Components()
    {
        var services = new ServiceCollection();
        var module = new DynamicApiModule();

        module.OnConfigureServices(services);

        services.Should().Contain(d =>
            d.ServiceType == typeof(ICapabilityEndpointRegistry)
            && d.ImplementationType == typeof(CapabilityEndpointRegistry));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IRegistryValidationEngine<CapabilityEndpointDescriptor>));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IRegistryValidator<CapabilityEndpointDescriptor>)
            && d.ImplementationType == typeof(CapabilityEndpointDescriptorValidator));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDescriptorRelationshipExtractor)
            && d.ImplementationType == typeof(CapabilityEndpointRelationshipExtractor));
    }
}
```

- [ ] **Step 2: Run DI test to verify it fails**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter FullyQualifiedName~CapabilityEndpointDynamicApiModuleTests
```

Expected: fail because module does not register the components.

- [ ] **Step 3: Register metadata components**

Modify `src/Framework/Api/CrestCreates.DynamicApi/Modules/DynamicApiModule.cs`:

```csharp
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.DynamicApi.Modules;

[CrestModule]
public class DynamicApiModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<ICapabilityEndpointRegistry, CapabilityEndpointRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<CapabilityEndpointDescriptor>,
            RegistryValidationEngine<CapabilityEndpointDescriptor>>();
        services.AddSingleton<IRegistryValidator<CapabilityEndpointDescriptor>,
            CapabilityEndpointDescriptorValidator>();
        services.AddSingleton<IDescriptorRelationshipExtractor,
            CapabilityEndpointRelationshipExtractor>();
    }
}
```

- [ ] **Step 4: Run focused tests**

Run:

```bash
dotnet test tests/Framework/Web/CrestCreates.Web.Tests --filter "FullyQualifiedName~CapabilityEndpoint"
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~CapabilityEndpoint"
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~DynamicApiEndpointDescriptorKindPolicyTests|FullyQualifiedName~DescriptorKindDenyTests"
```

Expected: pass.

- [ ] **Step 5: Run build and boundary verification**

Run:

```bash
dotnet build
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: pass. If boundary tests fail on the `Metadata -> DynamicApi.Abstractions` hash-profile dependency, stop and revise with the user before claiming implementation complete.

- [ ] **Step 6: Commit DI registration**

```bash
git add src/Framework/Api/CrestCreates.DynamicApi/Modules/DynamicApiModule.cs \
        tests/Framework/Web/CrestCreates.Web.Tests/DynamicApi/CapabilityEndpointDynamicApiModuleTests.cs
git commit -m "feat: register dynamic api endpoint descriptor metadata"
```

## Self-Review Checklist

- [ ] Spec coverage: descriptor model, required capability ref, authorization validation, route convention, registry, relationship, topology, hash, control-plane validity, DI, and runtime non-goals are each mapped to tasks.
- [ ] Placeholder scan: no placeholder markers or vague test instructions remain.
- [ ] Type consistency: `CapabilityEndpointDescriptor`, `ICapabilityEndpointRegistry`, `CapabilityEndpointRelationshipExtractor`, and hash profile names are consistent across tasks.
- [ ] Boundary risk is explicit: Task 4 avoids `Metadata -> DynamicApi` implementation dependency by introducing `DynamicApi.Abstractions`, and still requires boundary test verification rather than weakening boundaries.
- [ ] Runtime boundary is explicit: no task maps endpoints, invokes `CapabilityDispatcher`, changes generated Dynamic API endpoint code, or touches MVC/controller generation.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-03-phase-8b-dynamic-api-descriptor.md`. Two execution options:

**1. Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** - execute tasks in this session using executing-plans, batch execution with checkpoints.
