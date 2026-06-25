# Semantic String Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralize stable semantic strings into feature-owned constant classes and typed value objects while preserving string wire contracts and compile-time `const` usage.

**Architecture:** Add small string-backed semantic value objects in `CrestCreates.Core.Abstractions.Identity`, then migrate owning features to expose `XxxValue` constants plus typed `Xxx` properties. Runtime call sites use typed values where APIs support them; attributes, Roslyn diagnostics, generated code, DTOs, persistence, and wire contracts continue to use strings. A final architecture guard prevents high-value semantic literals from returning.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Roslyn Source Generators, MSBuild solution `CrestCreates.slnx`, repository boundary tests.

## Global Constraints

- All semantic value objects use namespace `CrestCreates.Core.Abstractions.Identity`.
- Semantic value objects are `readonly record struct` types with `Value`, `IsEmpty`, `RequireValue()`, `ToString()`, and implicit conversion to `string`.
- Semantic value objects must not provide implicit conversion from `string` to the value object.
- Runtime entry points that execute, persist, publish, or expose semantic values must reject empty/default semantic values where correctness matters.
- Constant classes expose `public const string XxxValue = "..."` and `public static <Type> Xxx { get; } = new(XxxValue)`.
- Attribute, source generator, generated-code, and Roslyn `DiagnosticDescriptor` sites use `XxxValue`.
- Existing wire DTOs, EF entities, JSON contracts, and persistence models remain string-compatible.
- Do not migrate HTTP methods, route fragments, JSON property names, configuration section names, database names, transport headers, or log templates unless they are official platform identities.
- Feature-specific constants stay in their owning abstraction or shared project.
- Prefer business feature names over deepest folder names, such as `DescriptorActivationErrorCodes` instead of `ActivationErrorCodes`.
- Generators may emit typed semantic properties only when the target compilation can resolve the value object type; otherwise emit `XxxValue` only and report or skip typed member generation non-fatally.
- The architecture guard scans only `src/**/*.cs` and `tests/**/*.cs`, skips generated output, snapshots, migrations, docs, `bin/`, and `obj/`, and supports `// semantic-string-guard: allow`.

---

## File Structure

Create:

- `src/Core/CrestCreates.Core.Abstractions/Identity/SemanticStringValue.cs` internal shared helper for validation and default handling.
- `src/Core/CrestCreates.Core.Abstractions/Identity/ErrorCode.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/DiagnosticCode.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/EventName.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/PermissionName.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/PolicyName.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/CapabilityId.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/WorkflowId.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/HumanTaskId.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/DescriptorId.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/VersionKey.cs`
- `src/Core/CrestCreates.Core.Abstractions/Identity/MessageTemplateId.cs`
- `tests/Core/CrestCreates.Core.Abstractions.Tests/CrestCreates.Core.Abstractions.Tests.csproj`
- `tests/Core/CrestCreates.Core.Abstractions.Tests/Identity/SemanticValueObjectTests.cs`
- `src/Framework/Ddd/CrestCreates.Domain.Shared/Exceptions/CrestErrorCodes.cs`
- `src/Framework/Ddd/CrestCreates.Domain.Shared/Permissions/CrestPermissionNames.cs`
- `src/Framework/Ddd/CrestCreates.Domain.Shared/Permissions/CrestPolicyNames.cs`
- `src/Metadata/CrestCreates.Schema.Abstractions/SchemaValidationErrorCodes.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/DescriptorActivationDiagnosticCodes.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/DescriptorActivationHumanTaskIds.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ReportBuilder/DescriptorActivationMessageTemplateIds.cs`
- `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashDiagnosticCodes.cs`
- `src/Tooling/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingDiagnosticCodes.cs`
- `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/SemanticStringGuardTests.cs`

Modify:

- No solution file change is required for the new Core.Abstractions test project because this plan runs it by project path.
- `src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`
- `src/Metadata/CrestCreates.Schema.Abstractions/CrestCreates.Schema.Abstractions.csproj`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/CrestCreates.Agent.ControlPlane.Abstractions.csproj`
- `src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj` is not expected to reference Core.Abstractions; generator code should emit fully qualified type names as text and resolve them from the target compilation.
- `tests/Framework/Ddd/CrestCreates.Domain.Tests/CrestCreates.Domain.Tests.csproj`
- `tests/Metadata/Core/CrestCreates.Schema.Tests/CrestCreates.Schema.Tests.csproj`
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/CrestCreates.Agent.ControlPlane.Tests.csproj`
- `tests/Tooling/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj`
- `src/Framework/Ddd/CrestCreates.Domain.Shared/Exceptions/CrestException.cs`
- `src/Framework/Ddd/CrestCreates.Domain.Shared/Exceptions/CrestBusinessException.cs`
- `src/Framework/Ddd/CrestCreates.Domain.Shared/Features/FeatureManagementErrorCodes.cs`
- `src/Framework/Ddd/CrestCreates.Application/Features/FeatureManagementExceptionFactory.cs`
- `src/Framework/Ddd/CrestCreates.Application/Features/FeatureManagementPermissions.cs`
- `src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/PermissionPolicies.cs`
- `src/Metadata/CrestCreates.Schema/SchemaValidator.cs`
- `src/Metadata/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticCodes.cs`
- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnosticCode.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolPermissionName.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentToolAuthorizationService.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultDescriptorActivationRequestService.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultActivationReviewOrchestrator.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DescriptorActivationReviewHumanTaskEventHandler.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/InMemoryRuntimeActivationGate.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewMessageTemplateCatalog.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs`
- `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashDiagnostics.cs`
- `src/Tooling/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingDiagnostics.cs`
- `src/Tooling/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`
- `src/Tooling/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`
- Existing tests that assert official semantic string values under `tests/Framework`, `tests/Metadata`, `tests/Runtime`, and `tests/Tooling`; each migration task below names the concrete test project to run.

Do not modify generated output under `obj/`, `bin/`, snapshots, migrations, or docs except this implementation plan.

---

### Task 1: Core Semantic Value Objects

**Files:**
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/SemanticStringValue.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/ErrorCode.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/DiagnosticCode.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/EventName.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/PermissionName.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/PolicyName.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/CapabilityId.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/WorkflowId.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/HumanTaskId.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/DescriptorId.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/VersionKey.cs`
- Create: `src/Core/CrestCreates.Core.Abstractions/Identity/MessageTemplateId.cs`
- Create: `tests/Core/CrestCreates.Core.Abstractions.Tests/CrestCreates.Core.Abstractions.Tests.csproj`
- Create: `tests/Core/CrestCreates.Core.Abstractions.Tests/Identity/SemanticValueObjectTests.cs`

**Interfaces:**
- Produces: `readonly record struct ErrorCode`, `DiagnosticCode`, `EventName`, `PermissionName`, `PolicyName`, `CapabilityId`, `WorkflowId`, `HumanTaskId`, `DescriptorId`, `VersionKey`, `MessageTemplateId`.
- Produces: each value object has constructor `(string value)`, `string? Value`, `bool IsEmpty`, `string RequireValue()`, `override string ToString()`, and `static implicit operator string(<Type> value)`.
- Consumes: no project dependencies beyond `System`.

- [ ] **Step 1: Write failing tests for explicit construction and default handling**

Create `tests/Core/CrestCreates.Core.Abstractions.Tests/CrestCreates.Core.Abstractions.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CrestCreates.Core.Abstractions.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Core.Abstractions.Tests</AssemblyName>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
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
    <ProjectReference Include="../../../src/Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

Create `tests/Core/CrestCreates.Core.Abstractions.Tests/Identity/SemanticValueObjectTests.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;
using FluentAssertions;

namespace CrestCreates.Core.Abstractions.Tests.Identity;

public class SemanticValueObjectTests
{
    [Fact]
    public void ErrorCode_Rejects_Whitespace()
    {
        var act = () => new ErrorCode(" ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("value");
    }

    [Fact]
    public void ErrorCode_Default_Is_Empty_And_RequireValue_Throws()
    {
        var code = default(ErrorCode);

        code.IsEmpty.Should().BeTrue();
        code.ToString().Should().BeEmpty();
        var act = () => code.RequireValue();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Error code is empty.");
    }

    [Fact]
    public void ErrorCode_Converts_To_String_Without_String_To_ErrorCode_Implicit_Conversion()
    {
        var code = new ErrorCode("Crest.FeatureManagement.InvalidValue");

        string value = code;

        value.Should().Be("Crest.FeatureManagement.InvalidValue");
    }

    [Fact]
    public void All_Semantic_Value_Objects_Expose_Required_Runtime_Shape()
    {
        new DiagnosticCode("CCHASH001").RequireValue().Should().Be("CCHASH001");
        new EventName("activation.rejected").RequireValue().Should().Be("activation.rejected");
        new PermissionName("agent.draft.create").RequireValue().Should().Be("agent.draft.create");
        new PolicyName("Permission:agent.draft.create").RequireValue().Should().Be("Permission:agent.draft.create");
        new CapabilityId("capability.test").RequireValue().Should().Be("capability.test");
        new WorkflowId("workflow.test").RequireValue().Should().Be("workflow.test");
        new HumanTaskId("descriptor-activation-review").RequireValue().Should().Be("descriptor-activation-review");
        new DescriptorId("schema.T1").RequireValue().Should().Be("schema.T1");
        new VersionKey("event.test:v1").RequireValue().Should().Be("event.test:v1");
        new MessageTemplateId("report.activation.eligible").RequireValue().Should().Be("report.activation.eligible");
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```bash
dotnet test tests/Core/CrestCreates.Core.Abstractions.Tests
```

Expected: FAIL because `CrestCreates.Core.Abstractions.Identity` types do not exist.

- [ ] **Step 3: Implement the shared helper and one value object**

Create `src/Core/CrestCreates.Core.Abstractions/Identity/SemanticStringValue.cs`:

```csharp
namespace CrestCreates.Core.Abstractions.Identity;

internal static class SemanticStringValue
{
    public static string Validate(string value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{displayName} cannot be empty.", nameof(value));
        }

        return value;
    }

    public static string Require(string? value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{displayName} is empty.");
        }

        return value;
    }
}
```

Create `src/Core/CrestCreates.Core.Abstractions/Identity/ErrorCode.cs`:

```csharp
namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct ErrorCode
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public ErrorCode(string value)
    {
        Value = SemanticStringValue.Validate(value, "Error code");
    }

    public string RequireValue() => SemanticStringValue.Require(Value, "Error code");

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(ErrorCode code) => code.RequireValue();
}
```

- [ ] **Step 4: Implement the remaining value objects with matching semantics**

Each file uses namespace `CrestCreates.Core.Abstractions.Identity` and the same shape as `ErrorCode`. Display names are:

```text
Diagnostic code
Event name
Permission name
Policy name
Capability id
Workflow id
Human task id
Descriptor id
Version key
Message template id
```

For example, `PermissionName.cs`:

```csharp
namespace CrestCreates.Core.Abstractions.Identity;

public readonly record struct PermissionName
{
    public string? Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public PermissionName(string value)
    {
        Value = SemanticStringValue.Validate(value, "Permission name");
    }

    public string RequireValue() => SemanticStringValue.Require(Value, "Permission name");

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(PermissionName name) => name.RequireValue();
}
```

- [ ] **Step 5: Run tests to verify GREEN**

Run:

```bash
dotnet test tests/Core/CrestCreates.Core.Abstractions.Tests
```

Expected: PASS.

- [ ] **Step 6: Build Core.Abstractions**

Run:

```bash
dotnet build src/Core/CrestCreates.Core.Abstractions
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Core/CrestCreates.Core.Abstractions tests/Core/CrestCreates.Core.Abstractions.Tests
git commit -m "feat: add semantic identity value objects"
```

---

### Task 2: Typed Exception and Policy Entry Points

**Files:**
- Modify: `src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`
- Modify: `src/Framework/Ddd/CrestCreates.Domain.Shared/Exceptions/CrestException.cs`
- Modify: `src/Framework/Ddd/CrestCreates.Domain.Shared/Exceptions/CrestBusinessException.cs`
- Create: `src/Framework/Ddd/CrestCreates.Domain.Shared/Exceptions/CrestErrorCodes.cs`
- Create: `src/Framework/Ddd/CrestCreates.Domain.Shared/Permissions/CrestPermissionNames.cs`
- Create: `src/Framework/Ddd/CrestCreates.Domain.Shared/Permissions/CrestPolicyNames.cs`
- Modify: `src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization/PermissionPolicies.cs`
- Modify: `tests/Framework/Ddd/CrestCreates.Domain.Tests/CrestCreates.Domain.Tests.csproj`
- Create: `tests/Framework/Ddd/CrestCreates.Domain.Tests/Exceptions/CrestBusinessExceptionTests.cs`

**Interfaces:**
- Consumes: `ErrorCode`, `PermissionName`, `PolicyName` from Task 1.
- Produces: `CrestException.ErrorCodeValue`, typed exception constructors, `CrestErrorCodes`, `CrestPermissionNames`, and `CrestPolicyNames`.

- [ ] **Step 1: Write failing exception tests**

Create `tests/Framework/Ddd/CrestCreates.Domain.Tests/Exceptions/CrestBusinessExceptionTests.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Domain.Shared.Exceptions;
using FluentAssertions;

namespace CrestCreates.Domain.Tests.Exceptions;

public class CrestBusinessExceptionTests
{
    [Fact]
    public void Constructor_Accepts_Typed_ErrorCode_And_Preserves_Wire_Code()
    {
        var exception = new CrestBusinessException(
            new ErrorCode("Crest.FeatureManagement.InvalidValue"),
            "Invalid feature value.");

        exception.ErrorCode.Should().Be("Crest.FeatureManagement.InvalidValue");
        exception.ErrorCodeValue.Should().Be(new ErrorCode("Crest.FeatureManagement.InvalidValue"));
        exception.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public void Constructor_Rejects_Default_ErrorCode()
    {
        var act = () => new CrestBusinessException(default(ErrorCode), "Invalid feature value.");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Error code is empty.");
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```bash
dotnet test tests/Framework/Ddd/CrestCreates.Domain.Tests --filter "FullyQualifiedName~CrestBusinessExceptionTests"
```

Expected: FAIL because `ErrorCodeValue` and typed constructors do not exist, or the project does not yet reference Core.Abstractions.

- [ ] **Step 3: Add Core.Abstractions reference to Domain.Shared**

Modify `src/Framework/Ddd/CrestCreates.Domain.Shared/CrestCreates.Domain.Shared.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../../../Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
</ItemGroup>
```

If Step 7 fails because the test project cannot resolve `ErrorCode`, add this direct test project reference to `tests/Framework/Ddd/CrestCreates.Domain.Tests/CrestCreates.Domain.Tests.csproj`:

```xml
<ProjectReference Include="../../../../src/Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
```

- [ ] **Step 4: Implement typed exception overloads**

Modify `CrestException.cs` to add `using CrestCreates.Core.Abstractions.Identity;`, store `ErrorCodeValue`, and keep `ErrorCode` as the string wire contract:

```csharp
public string ErrorCode { get; }

public ErrorCode ErrorCodeValue { get; }
```

The existing string constructor delegates through `new ErrorCode(errorCode)`. Add a protected typed constructor:

```csharp
protected CrestException(
    ErrorCode errorCode,
    int httpStatusCode,
    string message,
    string? details = null,
    Exception? innerException = null)
    : base(message, innerException)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(message);

    ErrorCodeValue = errorCode;
    ErrorCode = errorCode.RequireValue();
    HttpStatusCode = httpStatusCode;
    Details = details;
}
```

Modify `CrestBusinessException.cs` to add:

```csharp
public CrestBusinessException(
    ErrorCode errorCode,
    string message,
    string? details = null,
    Exception? innerException = null)
    : base(errorCode, 400, message, details, innerException)
{
}
```

- [ ] **Step 5: Add platform error and permission constants**

Create `CrestErrorCodes.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Domain.Shared.Exceptions;

public static class CrestErrorCodes
{
    public const string InternalErrorValue = "Crest.InternalError";
    public static ErrorCode InternalError { get; } = new(InternalErrorValue);

    public const string AuthUnauthorizedValue = "Crest.Auth.Unauthorized";
    public static ErrorCode AuthUnauthorized { get; } = new(AuthUnauthorizedValue);

    public const string AuthForbiddenValue = "Crest.Auth.Forbidden";
    public static ErrorCode AuthForbidden { get; } = new(AuthForbiddenValue);

    public const string ValidationFailedValue = "Crest.Validation.Failed";
    public static ErrorCode ValidationFailed { get; } = new(ValidationFailedValue);

    public const string ConcurrencyConflictValue = "Crest.Concurrency.Conflict";
    public static ErrorCode ConcurrencyConflict { get; } = new(ConcurrencyConflictValue);

    public const string ConcurrencyPreconditionRequiredValue = "Crest.Concurrency.PreconditionRequired";
    public static ErrorCode ConcurrencyPreconditionRequired { get; } = new(ConcurrencyPreconditionRequiredValue);
}
```

Create `CrestPermissionNames.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Domain.Shared.Permissions;

public static class CrestPermissionNames
{
    public const string CreateValue = "Create";
    public static PermissionName Create { get; } = new(CreateValue);

    public const string UpdateValue = "Update";
    public static PermissionName Update { get; } = new(UpdateValue);

    public const string DeleteValue = "Delete";
    public static PermissionName Delete { get; } = new(DeleteValue);

    public const string ViewValue = "View";
    public static PermissionName View { get; } = new(ViewValue);

    public const string ManageValue = "Manage";
    public static PermissionName Manage { get; } = new(ManageValue);
}
```

Create `CrestPolicyNames.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Domain.Shared.Permissions;

public static class CrestPolicyNames
{
    public const string PermissionPrefixValue = "Permission";
    public static PolicyName PermissionPrefix { get; } = new(PermissionPrefixValue);

    public const string PermissionAllPrefixValue = "PermissionAll";
    public static PolicyName PermissionAllPrefix { get; } = new(PermissionAllPrefixValue);
}
```

- [ ] **Step 6: Add typed policy helper overloads**

Modify `PermissionPolicies.cs` to keep existing string overloads and add typed overloads:

```csharp
public static string CreatePolicyName(params PermissionName[] permissions)
{
    return CreatePolicyName(permissions.Select(p => p.RequireValue()).ToArray());
}

public static string CreateAllPolicyName(params PermissionName[] permissions)
{
    return CreateAllPolicyName(permissions.Select(p => p.RequireValue()).ToArray());
}
```

Use `using CrestCreates.Core.Abstractions.Identity;` and `System.Linq`.

- [ ] **Step 7: Run targeted tests**

Run:

```bash
dotnet test tests/Framework/Ddd/CrestCreates.Domain.Tests --filter "FullyQualifiedName~CrestBusinessExceptionTests"
dotnet build src/Framework/Ddd/CrestCreates.Domain.Shared
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Framework/Ddd/CrestCreates.Domain.Shared src/Framework/Infrastructure/CrestCreates.Infrastructure/Authorization tests/Framework/Ddd/CrestCreates.Domain.Tests
git commit -m "feat: add typed semantic exception entry points"
```

---

### Task 3: P0 Framework, Schema, and Metadata Constants

**Files:**
- Modify: `src/Framework/Ddd/CrestCreates.Domain.Shared/Features/FeatureManagementErrorCodes.cs`
- Modify: `src/Framework/Ddd/CrestCreates.Application/Features/FeatureManagementExceptionFactory.cs`
- Modify: `src/Framework/Ddd/CrestCreates.Application/Features/FeatureManagementPermissions.cs`
- Modify: `src/Metadata/CrestCreates.Schema.Abstractions/CrestCreates.Schema.Abstractions.csproj`
- Create: `src/Metadata/CrestCreates.Schema.Abstractions/SchemaValidationErrorCodes.cs`
- Modify: `src/Metadata/CrestCreates.Schema/SchemaValidator.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.ContextPack.Abstractions/MetadataContextPackDiagnosticCodes.cs`
- Modify: `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/DescriptorPackageDiagnosticCode.cs`
- Modify: feature-management tests under `tests/Framework/Ddd/CrestCreates.Application.Tests`
- Modify: `tests/Metadata/Core/CrestCreates.Schema.Tests/SchemaValidatorTests.cs`
- Modify: `tests/Metadata/Core/CrestCreates.Metadata.ContextPack.Tests/MetadataContextPackBuilderTests.cs`
- Modify: tests under `tests/Metadata/Core/CrestCreates.Metadata.Tests` that assert `DescriptorPackageDiagnosticCode` values.

**Interfaces:**
- Consumes: `ErrorCode`, `DiagnosticCode`, `PermissionName`.
- Produces: migrated P0 constants for framework feature management, schema validation, metadata context pack diagnostics, and descriptor package diagnostics.

- [ ] **Step 1: Write failing schema tests for centralized error codes**

Modify `tests/Metadata/Core/CrestCreates.Schema.Tests/SchemaValidatorTests.cs` so assertions reference `SchemaValidationErrorCodes.FieldRequiredValue`, `TypeMismatchValue`, `MaxLengthExceededValue`, `PatternMismatchValue`, `NullNotAllowedValue`, `MinValueNotMetValue`, and `MaxValueExceededValue`.

Example replacement:

```csharp
result.Errors[0].ErrorCode.Should().Be(SchemaValidationErrorCodes.FieldRequiredValue);
```

- [ ] **Step 2: Run schema tests to verify RED**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests --filter "FullyQualifiedName~SchemaValidatorTests"
```

Expected: FAIL because `SchemaValidationErrorCodes` does not exist.

- [ ] **Step 3: Add Core.Abstractions reference to Schema.Abstractions**

Add `ProjectReference` to `src/Metadata/CrestCreates.Schema.Abstractions/CrestCreates.Schema.Abstractions.csproj`:

```xml
<ProjectReference Include="../Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
```

- [ ] **Step 4: Create schema validation error code constants**

Create `SchemaValidationErrorCodes.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Schema.Abstractions;

public static class SchemaValidationErrorCodes
{
    public const string FieldRequiredValue = "FIELD_REQUIRED";
    public static ErrorCode FieldRequired { get; } = new(FieldRequiredValue);

    public const string TypeMismatchValue = "TYPE_MISMATCH";
    public static ErrorCode TypeMismatch { get; } = new(TypeMismatchValue);

    public const string MaxLengthExceededValue = "MAX_LENGTH_EXCEEDED";
    public static ErrorCode MaxLengthExceeded { get; } = new(MaxLengthExceededValue);

    public const string MinLengthNotMetValue = "MIN_LENGTH_NOT_MET";
    public static ErrorCode MinLengthNotMet { get; } = new(MinLengthNotMetValue);

    public const string PatternMismatchValue = "PATTERN_MISMATCH";
    public static ErrorCode PatternMismatch { get; } = new(PatternMismatchValue);

    public const string MaxValueExceededValue = "MAX_VALUE_EXCEEDED";
    public static ErrorCode MaxValueExceeded { get; } = new(MaxValueExceededValue);

    public const string MinValueNotMetValue = "MIN_VALUE_NOT_MET";
    public static ErrorCode MinValueNotMet { get; } = new(MinValueNotMetValue);

    public const string NullNotAllowedValue = "NULL_NOT_ALLOWED";
    public static ErrorCode NullNotAllowed { get; } = new(NullNotAllowedValue);
}
```

- [ ] **Step 5: Migrate schema validator call sites**

Modify `SchemaValidator.cs` to use `SchemaValidationErrorCodes.*Value` for every `SchemaValidationError.ErrorCode` assignment.

- [ ] **Step 6: Migrate feature management constants**

Modify `FeatureManagementErrorCodes.cs` to move previous string member names to `XxxValue` and expose typed properties under the old semantic names:

```csharp
public const string UndefinedFeatureValue = "Crest.FeatureManagement.UndefinedFeature";
public static ErrorCode UndefinedFeature { get; } = new(UndefinedFeatureValue);
```

Update call sites that need strings to use `UndefinedFeatureValue`, `InvalidValueValue`, `UnsupportedScopeValue`, `CrossTenantAccessDeniedValue`, or `MissingTenantContextValue`. Update call sites constructing `CrestBusinessException` to use the typed properties.

Modify `FeatureManagementExceptionFactory.cs` to call `new CrestBusinessException(FeatureManagementErrorCodes.InvalidValue, ...)`.

Modify `FeatureManagementPermissions.cs` to expose `ReadValue`, `ManageGlobalValue`, `ManageTenantValue` plus typed `PermissionName` properties.

- [ ] **Step 7: Migrate metadata diagnostic constant classes to typed shape**

Modify `MetadataContextPackDiagnosticCodes.cs` and `DescriptorPackageDiagnosticCode.cs`:

```csharp
public const string FocusNotFoundValue = "CTXPACK_FOCUS_NOT_FOUND";
public static DiagnosticCode FocusNotFound { get; } = new(FocusNotFoundValue);
```

Then update call sites that assign string `Code` to use `FocusNotFoundValue`. Update tests that compare codes to use `FocusNotFoundValue`.

- [ ] **Step 8: Run targeted tests**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests --filter "FullyQualifiedName~SchemaValidatorTests"
dotnet test tests/Metadata/Core/CrestCreates.Metadata.ContextPack.Tests --filter "FullyQualifiedName~MetadataContextPackBuilderTests"
dotnet test tests/Framework/Ddd/CrestCreates.Application.Tests --filter "FullyQualifiedName~Feature"
```

Expected: PASS. If the feature filter is too broad, run the concrete feature management test classes changed in Step 6.

- [ ] **Step 9: Commit**

```bash
git add src/Framework/Ddd src/Metadata tests/Framework/Ddd tests/Metadata
git commit -m "refactor: centralize framework and metadata semantic codes"
```

---

### Task 4: P0 Agent Activation, Permissions, and Message Templates

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/CrestCreates.Agent.ControlPlane.Abstractions.csproj`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolPermissionName.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/DescriptorActivationDiagnosticCodes.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Activation/DescriptorActivationHumanTaskIds.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ReportBuilder/DescriptorActivationMessageTemplateIds.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentToolAuthorizationService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultDescriptorActivationRequestService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DefaultActivationReviewOrchestrator.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/DescriptorActivationReviewHumanTaskEventHandler.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/InMemoryRuntimeActivationGate.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewMessageTemplateCatalog.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs`
- Modify: tests in `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`

**Interfaces:**
- Consumes: `DiagnosticCode`, `HumanTaskId`, `MessageTemplateId`, `PermissionName`.
- Produces: typed Agent permission names, descriptor activation diagnostic codes, human task ids, and report message template ids.

- [ ] **Step 1: Write failing tests against new symbols**

Update representative tests:

- `DescriptorActivationRequestServiceTests` assertions use `DescriptorActivationDiagnosticCodes.InvalidStatusForRejectionValue` and other matching constants.
- `ActivationReviewOrchestratorTests` uses `DescriptorActivationHumanTaskIds.ReviewValue`.
- `DescriptorReviewReportBuilderTests` uses `DescriptorActivationMessageTemplateIds.EligibleValue` and `BlockedValue`.

Example:

```csharp
result.Diagnostics.Should().Contain(d =>
    d.Code == DescriptorActivationDiagnosticCodes.InvalidStatusForRejectionValue);
```

- [ ] **Step 2: Run targeted tests to verify RED**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FullyQualifiedName~DescriptorActivationRequestServiceTests|FullyQualifiedName~ActivationReviewOrchestratorTests|FullyQualifiedName~DescriptorReviewReportBuilderTests"
```

Expected: FAIL because new constant classes do not exist.

- [ ] **Step 3: Add Core.Abstractions reference to Agent Control Plane abstractions**

Modify `CrestCreates.Agent.ControlPlane.Abstractions.csproj`:

```xml
<ProjectReference Include="../../../Core/CrestCreates.Core.Abstractions/CrestCreates.Core.Abstractions.csproj" />
```

Verify relative path before committing.

- [ ] **Step 4: Create descriptor activation diagnostic constants**

Create `DescriptorActivationDiagnosticCodes.cs` with these exact values:

```text
ACTIVATION_BINDING_HASHES_REQUIRED
ACTIVATION_BINDING_SNAPSHOT_REQUIRED
ACTIVATION_BLOCKED_BY_GOVERNANCE
ACTIVATION_CANNOT_CANCEL
ACTIVATION_EVIDENCE_PREVIEW_DRAFT_MISMATCH
ACTIVATION_EVIDENCE_PREVIEW_NOT_FOUND
ACTIVATION_EVIDENCE_STALE
ACTIVATION_GATE_BLOCKED
ACTIVATION_GATE_INVALID_STATE
ACTIVATION_GOVERNANCE_BLOCKED
ACTIVATION_HANDOFF_DENIED
ACTIVATION_INCOMPLETE_BINDING
ACTIVATION_INVALID_STATUS_FOR_APPROVAL
ACTIVATION_INVALID_STATUS_FOR_REJECTION
ACTIVATION_PACKAGE_PREVIEW_DRAFT_MISMATCH
ACTIVATION_PACKAGE_PREVIEW_NOT_FOUND
ACTIVATION_REQUEST_TERMINAL
ACTIVATION_REQUIRES_HUMAN_REVIEW
ACTIVATION_REVIEW_DECISION_MISMATCH
ACTIVATION_REVIEW_ENVELOPE_MISMATCH
ACTIVATION_REVIEW_EVIDENCE_MISMATCH
ACTIVATION_REVIEW_NOT_REQUIRED
ACTIVATION_REVIEW_REQUEST_MISMATCH
ACTIVATION_REVIEW_RESULT_DRAFT_MISMATCH
ACTIVATION_REVIEW_RESULT_NOT_FOUND
ACTIVATION_SELF_APPROVAL_FORBIDDEN
NOT_ACTIVATION_ELIGIBLE
RUNTIME_ACTIVATION_GATE_REJECTED
```

Use PascalCase member names. Include this starting shape and complete the class with one `XxxValue` constant and one typed `DiagnosticCode` property for each listed value:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

public static class DescriptorActivationDiagnosticCodes
{
    public const string BindingSnapshotRequiredValue = "ACTIVATION_BINDING_SNAPSHOT_REQUIRED";
    public static DiagnosticCode BindingSnapshotRequired { get; } = new(BindingSnapshotRequiredValue);

    public const string InvalidStatusForRejectionValue = "ACTIVATION_INVALID_STATUS_FOR_REJECTION";
    public static DiagnosticCode InvalidStatusForRejection { get; } = new(InvalidStatusForRejectionValue);

    public const string RuntimeActivationGateRejectedValue = "RUNTIME_ACTIVATION_GATE_REJECTED";
    public static DiagnosticCode RuntimeActivationGateRejected { get; } = new(RuntimeActivationGateRejectedValue);
}
```

After creating the class, verify no new activation code was missed:

```bash
rg -n "\"(ACTIVATION|RUNTIME_ACTIVATION|NOT_ACTIVATION)[A-Z0-9_]*|ACTIVATION_HANDOFF_DENIED\"" src/Runtime/Agent tests/Runtime/Agent -g '*.cs'
```

Expected: every value printed by the command is present in `DescriptorActivationDiagnosticCodes`.

- [ ] **Step 5: Create human task id and message template constants**

Create `DescriptorActivationHumanTaskIds.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

public static class DescriptorActivationHumanTaskIds
{
    public const string ReviewValue = "descriptor-activation-review";
    public static HumanTaskId Review { get; } = new(ReviewValue);
}
```

Create `DescriptorActivationMessageTemplateIds.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions.ReportBuilder;

public static class DescriptorActivationMessageTemplateIds
{
    public const string EligibleValue = "report.activation.eligible";
    public static MessageTemplateId Eligible { get; } = new(EligibleValue);

    public const string BlockedValue = "report.activation.blocked";
    public static MessageTemplateId Blocked { get; } = new(BlockedValue);
}
```

- [ ] **Step 6: Migrate Agent permission names**

Rename or migrate `AgentToolPermissionName` to plural `AgentToolPermissionNames` only if all call sites are updated in the same task. If rename blast radius is high, keep the existing class name for this task and migrate its members to `XxxValue` + typed `PermissionName` properties, then add a follow-up rename in a separate commit.

Required shape:

```csharp
public const string DraftCreateValue = "agent.draft.create";
public static PermissionName DraftCreate { get; } = new(DraftCreateValue);
```

Update string target call sites to use `DraftCreateValue`; update typed-compatible call sites to use `DraftCreate`.

- [ ] **Step 7: Replace Agent activation literals in implementation**

Replace:

- `Code = "ACTIVATION_..."` with `Code = DescriptorActivationDiagnosticCodes.XxxValue`
- `Code = "RUNTIME_ACTIVATION_GATE_REJECTED"` with `RuntimeActivationGateRejectedValue`
- `HumanTaskId = "descriptor-activation-review"` with `DescriptorActivationHumanTaskIds.ReviewValue`
- `@event.HumanTaskId != "descriptor-activation-review"` with `@event.HumanTaskId != DescriptorActivationHumanTaskIds.ReviewValue`
- `"report.activation.eligible"` and `"report.activation.blocked"` with message template constants
- `PermissionName = "agent...."` or direct `agent.*` literals with Agent permission constants

- [ ] **Step 8: Run targeted Agent tests**

Run:

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Runtime/Agent tests/Runtime/Agent
git commit -m "refactor: centralize agent semantic strings"
```

---

### Task 5: P1 Runtime and Metadata Identity Constants

**Files:**
- Create or modify constants in owning abstraction projects under:
  - `src/Metadata/CrestCreates.Metadata.Abstractions`
  - `src/Runtime/Capability/CrestCreates.Capability.Abstractions`
  - `src/Runtime/Workflow/CrestCreates.Workflow.Abstractions`
  - `src/Runtime/HumanTask/CrestCreates.HumanTask.Abstractions`
  - `src/Runtime/Eventing/CrestCreates.Event.Abstractions`
- Modify runtime call sites and tests that use stable descriptor, capability, workflow, human task, version key, and event ids.

**Interfaces:**
- Consumes: `CapabilityId`, `WorkflowId`, `HumanTaskId`, `DescriptorId`, `VersionKey`, `EventName`.
- Produces: P1 constant classes for stable runtime/metadata identity strings.

- [ ] **Step 1: Inventory P1 semantic literals**

Run and save the output in your task notes, not in the repository:

```bash
rg -n "\"(schema|capability|workflow|event|form|humantask)\\.[A-Za-z0-9_.-]+\"|\"ht_[0-9A-Za-z_-]+\"|\".*:v[0-9]+\"" src/Metadata src/Runtime tests/Metadata tests/Runtime -g '*.cs'
```

Classify matches into:

- official descriptor/capability/workflow/event/human task ids to migrate
- test data values to leave alone
- protocol or display strings to leave alone

- [ ] **Step 2: Write failing tests for official P1 constants**

For every official identity found in Step 1, update the existing test assertion that proves that identity to reference the new constant class. For descriptor activation human task id, Task 4 already covers the Agent-owned official id; do not duplicate it here.

Expected examples:

```csharp
request.HumanTaskId.Should().Be(DescriptorActivationHumanTaskIds.ReviewValue);
```

For metadata descriptor ids, add constants only where the value is a platform identity. One-off test data such as `"schema.T1"`, `"capability.test"`, or `"workflow.test"` remains inline test data and should not get a constant.

- [ ] **Step 3: Create owning P1 constant classes**

Use these names for subsystems with official identities found in Step 1:

- `MetadataDescriptorIds`
- `CapabilityRuntimeCapabilityIds`
- `WorkflowRuntimeWorkflowIds`
- `HumanTaskRuntimeHumanTaskIds`
- `EventRuntimeEventNames`
- `DescriptorVersionKeys`

Every class follows this concrete shape. Replace `PublicRegistryDescriptor` with the official identity name from Step 1 and replace `descriptor.public-registry` with its exact wire value:

```csharp
public const string PublicRegistryDescriptorValue = "descriptor.public-registry";
public static DescriptorId PublicRegistryDescriptor { get; } = new(PublicRegistryDescriptorValue);
```

Use the correct value object type for each class.

- [ ] **Step 4: Update implementation and tests**

Replace official P1 literals with constants. Keep one-off test scenario ids as literal test data.

- [ ] **Step 5: Run targeted tests**

Run:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests
dotnet test tests/Runtime/HumanTask/CrestCreates.HumanTask.Tests
dotnet test tests/Runtime/Eventing/CrestCreates.Event.Tests
```

Expected: PASS, except integration projects requiring external services should be skipped unless already configured locally.

- [ ] **Step 6: Commit**

```bash
git add src/Metadata src/Runtime tests/Metadata tests/Runtime
git commit -m "refactor: centralize runtime identity strings"
```

---

### Task 6: P2 Tooling Diagnostic IDs and Generated Permission Constants

**Files:**
- Create: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashDiagnosticCodes.cs`
- Create: `src/Tooling/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingDiagnosticCodes.cs`
- Modify: `src/Tooling/CrestCreates.CodeGenerator/CanonicalHashGenerator/CanonicalHashDiagnostics.cs`
- Modify: `src/Tooling/CrestCreates.CodeGenerator/ObjectMappingGenerator/ObjectMappingDiagnostics.cs`
- Modify: `src/Tooling/CrestCreates.CodeGenerator/CrudServiceGenerator/CrudServiceSourceGenerator.cs`
- Modify: `src/Tooling/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs`
- Modify: tests under `tests/Tooling/CrestCreates.CodeGenerator.Tests`

**Interfaces:**
- Consumes: `DiagnosticCode`, `PermissionName` when resolvable.
- Produces: centralized Roslyn diagnostic id constants and generated permission classes with `XxxValue` + typed `Xxx` shape.

- [ ] **Step 1: Write failing tests for diagnostic id constants**

Update `tests/Tooling/CrestCreates.CodeGenerator.Tests/CanonicalHashGenerator/CanonicalHashDiagnosticMainlineTests.cs` assertions from:

```csharp
Assert.Contains(errors, e => e.Id == "CCHASH015");
```

to:

```csharp
Assert.Contains(errors, e => e.Id == CanonicalHashDiagnosticCodes.UnionProfileMissingRequiredPropsValue);
```

Add `using CrestCreates.CodeGenerator.CanonicalHashGenerator;`.

- [ ] **Step 2: Run tooling tests to verify RED**

Run:

```bash
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~CanonicalHashDiagnosticMainlineTests"
```

Expected: FAIL because `CanonicalHashDiagnosticCodes` does not exist.

- [ ] **Step 3: Create diagnostic id constants and migrate DiagnosticDescriptor construction**

Create `CanonicalHashDiagnosticCodes.cs` with one constant for every `CCHASH###` id in `CanonicalHashDiagnostics.cs`:

```csharp
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.CodeGenerator.CanonicalHashGenerator;

public static class CanonicalHashDiagnosticCodes
{
    public const string UnclassifiedPropertyValue = "CCHASH001";
    public static DiagnosticCode UnclassifiedProperty { get; } = new(UnclassifiedPropertyValue);

    public const string UnionProfileMissingRequiredPropsValue = "CCHASH015";
    public static DiagnosticCode UnionProfileMissingRequiredProps { get; } = new(UnionProfileMissingRequiredPropsValue);
}
```

Complete every id through `CCHASH028`, preserving skipped historical ids by not reusing numbers.

Modify `CanonicalHashDiagnostics.cs` so each `new DiagnosticDescriptor(id: ...)` uses `CanonicalHashDiagnosticCodes.XxxValue`.

Repeat for Object Mapping diagnostics with `ObjectMappingDiagnosticCodes` and every `CCMAP###` id in `ObjectMappingDiagnostics.cs`.

- [ ] **Step 4: Update generated permission source shape**

Modify `CrudServiceSourceGenerator.cs` and `EntitySourceGenerator.cs` so generated permission classes emit:

```csharp
public const string CreateValue = "Book.Create";
public static global::CrestCreates.Core.Abstractions.Identity.PermissionName Create { get; } =
    new(CreateValue);
```

Before emitting the typed property, check that the target compilation can resolve:

```csharp
global::CrestCreates.Core.Abstractions.Identity.PermissionName
```

If it cannot, emit only `CreateValue`. If the generator has a diagnostic reporting helper available, report a non-fatal informational diagnostic; otherwise skip typed emission silently and cover that branch with a unit test.

- [ ] **Step 5: Update generator tests**

Update generator tests to assert generated output contains:

```csharp
public const string CreateValue = "Book.Create";
public static global::CrestCreates.Core.Abstractions.Identity.PermissionName Create { get; }
```

Also assert generated code no longer emits `public const string Create = "Book.Create";`.

- [ ] **Step 6: Run tooling tests**

Run:

```bash
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Tooling tests/Tooling
git commit -m "refactor: centralize tooling semantic diagnostics"
```

---

### Task 7: Architecture Guard

**Files:**
- Create: `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/SemanticStringGuardTests.cs`
- Modify: any remaining source/test files that violate the guard with official semantic literals

**Interfaces:**
- Consumes: migrated constants from Tasks 3-6.
- Produces: pattern-based regression guard for high-value semantic literals.

- [ ] **Step 1: Write failing guard test**

Create `SemanticStringGuardTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CrestCreates.DependencyBoundaries.Tests;

public class SemanticStringGuardTests
{
    private static readonly Regex[] ForbiddenPatterns =
    [
        new("\"ACTIVATION_[A-Z0-9_]+\"", RegexOptions.Compiled),
        new("\"CCHASH[0-9]{3}\"", RegexOptions.Compiled),
        new("\"CCMAP[0-9]{3}\"", RegexOptions.Compiled),
        new("\"FIELD_REQUIRED\"", RegexOptions.Compiled),
        new("\"descriptor-activation-review\"", RegexOptions.Compiled),
        new("\"agent\\.[a-z0-9_.-]+\"", RegexOptions.Compiled)
    ];

    [Fact]
    public void OfficialSemanticStrings_Are_Not_Inlined_Outside_Definition_Files()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => IsScannedSource(root, path))
            .ToArray();

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            if (text.Contains("semantic-string-guard: allow", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var pattern in ForbiddenPatterns)
            {
                if (pattern.IsMatch(text))
                {
                    violations.Add(relative);
                    break;
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Inline semantic string literals found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsScannedSource(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

        if (!relative.StartsWith("src/", StringComparison.Ordinal) &&
            !relative.StartsWith("tests/", StringComparison.Ordinal))
        {
            return false;
        }

        if (relative.Contains("/bin/", StringComparison.Ordinal) ||
            relative.Contains("/obj/", StringComparison.Ordinal) ||
            relative.Contains("/Generated/", StringComparison.Ordinal) ||
            relative.Contains("/Snapshots/", StringComparison.Ordinal) ||
            relative.Contains("/Migrations/", StringComparison.Ordinal))
        {
            return false;
        }

        return !IsDefinitionFile(relative);
    }

    private static bool IsDefinitionFile(string relative)
    {
        var name = Path.GetFileName(relative);
        return name.EndsWith("ErrorCodes.cs", StringComparison.Ordinal) ||
               name.EndsWith("DiagnosticCodes.cs", StringComparison.Ordinal) ||
               name.EndsWith("EventNames.cs", StringComparison.Ordinal) ||
               name.EndsWith("PermissionNames.cs", StringComparison.Ordinal) ||
               name.EndsWith("PermissionName.cs", StringComparison.Ordinal) ||
               name.EndsWith("PolicyNames.cs", StringComparison.Ordinal) ||
               name.EndsWith("CapabilityIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("WorkflowIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("HumanTaskIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("DescriptorIds.cs", StringComparison.Ordinal) ||
               name.EndsWith("VersionKeys.cs", StringComparison.Ordinal) ||
               name.EndsWith("MessageTemplateIds.cs", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
```

- [ ] **Step 2: Run boundary tests to verify RED if violations remain**

Run:

```bash
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests --filter "FullyQualifiedName~SemanticStringGuardTests"
```

Expected: FAIL if any official semantic literals remain inline, PASS if prior tasks fully migrated them.

- [ ] **Step 3: Fix remaining guard violations**

For every reported file:

- If the literal is an official semantic value, add it to the owning constant class and replace the inline literal.
- If the literal is fixture-only test data, move it into a local named constant in the test or add `// semantic-string-guard: allow` immediately above the intentional fixture block.
- If the literal is in generated output, update `IsScannedSource` to skip that generated directory rather than suppressing individual generated files.

- [ ] **Step 4: Run boundary tests to verify GREEN**

Run:

```bash
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Boundary src tests
git commit -m "test: guard semantic string centralization"
```

---

### Task 8: Final Verification and Documentation Update

**Files:**
- Modify: `memory.md` only if implementation completes successfully and the semantic string governance status should be recorded.

**Interfaces:**
- Consumes: all prior tasks.
- Produces: verified branch state. Produces a `memory.md` update only when Task 8 Step 1 and Step 2 pass and any skipped `dotnet test` work is explained.

- [ ] **Step 1: Run targeted test suites**

Run:

```bash
dotnet test tests/Core/CrestCreates.Core.Abstractions.Tests
dotnet test tests/Framework/Ddd/CrestCreates.Domain.Tests
dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

Expected: PASS.

- [ ] **Step 2: Run full build**

Run:

```bash
dotnet build
```

Expected: PASS.

- [ ] **Step 3: Run full tests if local environment supports it**

Run:

```bash
dotnet test
```

Expected: PASS, or document external-service/environment blockers explicitly in the final report.

- [ ] **Step 4: Update memory.md if governance is complete**

Add a concise entry under completed platform capabilities:

```markdown
### Semantic String Governance

Status: Completed for stable platform semantic identities.

Completed:
- Core semantic value objects in `CrestCreates.Core.Abstractions.Identity`
- Feature-owned `XxxValue` + typed `Xxx` constants
- Typed exception entry points with default-value rejection
- Generated permission and Roslyn diagnostic id centralization
- Boundary guard against high-value inline semantic literals
```

- [ ] **Step 5: Commit final docs if changed**

```bash
git add memory.md
git commit -m "docs: record semantic string governance closure"
```

Skip this commit if `memory.md` does not change.

- [ ] **Step 6: Final status**

Report:

- commits created
- tests run and results
- any skipped integration tests or environmental blockers
- remaining non-goal strings intentionally left inline
