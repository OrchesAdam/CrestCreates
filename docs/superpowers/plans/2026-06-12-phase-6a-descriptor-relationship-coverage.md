# Phase 6a — Descriptor Relationship Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the descriptor relationship coverage gap — every descriptor with outgoing refs exposes them through one uniform `IDescriptorRelationshipExtractor` path.

**Architecture:** Non-generic `IDescriptorRelationshipExtractor` per descriptor type, registered as singleton in DI. `IDescriptorRelationshipProvider` dispatches by concrete `DescriptorType` via `IsInstanceOfType`. Extractors replace `IRelationshipAwareDescriptor.GetRelationships()` (removed). `DescriptorRelationship` gains `Role`, `SourcePath`, `Strength`, `IsRuntimeBinding`.

**Tech Stack:** .NET 10, C# 13, xUnit + FluentAssertions, Microsoft.Extensions.DependencyInjection

**Design Spec:** `docs/superpowers/specs/2026-06-12-phase-6a-descriptor-relationship-coverage-design.md`

---

## File Structure

### New Files (11)

```
framework/src/CrestCreates.Metadata.Abstractions/
  RelationshipStrength.cs                    — Strong/Weak enum
  IDescriptorRelationshipExtractor.cs        — non-generic runtime interface
  DescriptorRelationshipExtractorBase.cs     — optional typed base class (AoT-safe `is` cast)
  IDescriptorRelationshipProvider.cs         — consumer-facing aggregation interface

framework/src/CrestCreates.Metadata/
  DefaultDescriptorRelationshipProvider.cs   — IsInstanceOfType dispatch, IEnumerable DI
  SchemaRelationshipExtractor.cs             — Schema.References[] → SchemaDescriptor refs

framework/src/CrestCreates.Form/
  FormRelationshipExtractor.cs               — Form.Schema → SchemaDescriptor

framework/src/CrestCreates.Capability/
  CapabilityRelationshipExtractor.cs         — InputSchema/OutputSchema/Produces/Consumes/SupersededById

framework/src/CrestCreates.Event/
  EventRelationshipExtractor.cs              — GeneratedEventDescriptor.PayloadSchemaRef → SchemaDescriptor

framework/src/CrestCreates.HumanTask/
  HumanTaskRelationshipExtractor.cs          — Interaction/InputSchema/OutputSchema/Outcomes

framework/src/CrestCreates.Workflow/
  WorkflowRelationshipExtractor.cs           — VariableSchema/CapabilityTarget/HumanTaskTarget/SubWorkflowTarget
```

### Modified Files (8)

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorRelationship.cs                  → add Uses, Triggers to RelationshipKind
                                            → add Role, SourcePath, Strength, IsRuntimeBinding to DescriptorRelationship record

framework/src/CrestCreates.Metadata/
  CapabilityDescriptor.cs                    → remove IRelationshipAwareDescriptor, remove GetRelationships()
  MetadataServiceCollectionExtensions.cs     → add AddRelationshipKernel() + register SchemaRelationshipExtractor

framework/src/CrestCreates.Form/
  FormServiceCollectionExtensions.cs         → register FormRelationshipExtractor

framework/src/CrestCreates.Capability/
  CapabilityServiceCollectionExtensions.cs   → register CapabilityRelationshipExtractor

framework/src/CrestCreates.Event/
  EventServiceCollectionExtensions.cs        → register EventRelationshipExtractor

framework/src/CrestCreates.HumanTask/
  HumanTaskServiceCollectionExtensions.cs    → register HumanTaskRelationshipExtractor

framework/src/CrestCreates.Workflow/
  WorkflowServiceCollectionExtensions.cs     → register WorkflowRelationshipExtractor
```

### Deleted Files (2)

```
framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs  → delete
framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs              → move to 99_RecycleBin/
```

### Test Files (10)

```
framework/test/CrestCreates.Metadata.Tests/
  RelationshipStrengthTests.cs
  RelationshipKindExtensionTests.cs
  DescriptorRelationshipEnhancementTests.cs
  SchemaRelationshipExtractorTests.cs
  DefaultDescriptorRelationshipProviderTests.cs

framework/test/CrestCreates.Form.Tests/
  FormRelationshipExtractorTests.cs

framework/test/CrestCreates.Capability.Tests/
  CapabilityRelationshipExtractorTests.cs

framework/test/CrestCreates.Event.Tests/
  EventRelationshipExtractorTests.cs

framework/test/CrestCreates.HumanTask.Tests/
  HumanTaskRelationshipExtractorTests.cs

framework/test/CrestCreates.Workflow.Tests/
  WorkflowRelationshipExtractorTests.cs
```

---

### Task 0: Core Types — RelationshipKind, DescriptorRelationship, RelationshipStrength, Interfaces

**Files:**
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/RelationshipStrength.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRelationshipExtractor.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationshipExtractorBase.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IDescriptorRelationshipProvider.cs`
- Delete: `framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs`

- [ ] **Step 1: Extend RelationshipKind with Uses and Triggers**

Edit `framework/src/CrestCreates.Metadata.Abstractions/DescriptorRelationship.cs` — add `Uses` and `Triggers` to the enum:

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Non-generic descriptor reference with namespace and id.
/// Used in relationship declarations. The generic DescriptorRef{TDescriptor} remains for typed refs.
/// </summary>
public readonly record struct DescriptorRef(
    string Namespace,
    string Id,
    int? Version = null) : IDescriptorRef
{
    public string FullId => $"{Namespace}.{Id}";
}

public sealed record DescriptorRelationship(
    DescriptorRef From,
    DescriptorRef To,
    RelationshipKind Kind,
    string? Role = null,
    string? SourcePath = null,
    RelationshipStrength Strength = RelationshipStrength.Strong,
    bool IsRuntimeBinding = false);

public enum RelationshipKind
{
    Produces,
    Consumes,
    DependsOn,
    References,
    Uses,
    Triggers
}
```

- [ ] **Step 2: Create RelationshipStrength.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public enum RelationshipStrength
{
    /// <summary>Descriptor breaks without this relationship (missing schema, missing target).</summary>
    Strong,

    /// <summary>Optional or informational (event production, superseded-by, unsupported features).</summary>
    Weak
}
```

- [ ] **Step 3: Create IDescriptorRelationshipExtractor.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Per-descriptor-type relationship extractor. One implementation per concrete descriptor type.
/// Registered as non-generic singleton in DI. Provider dispatches by DescriptorType.
/// Singleton, stateless, receives typed registry references via constructor DI if needed.
/// </summary>
public interface IDescriptorRelationshipExtractor
{
    /// <summary>Which DescriptorKind this extractor handles.</summary>
    DescriptorKind SupportedKind { get; }

    /// <summary>The concrete descriptor type this extractor handles (e.g., typeof(CapabilityDescriptor)).</summary>
    Type DescriptorType { get; }

    /// <summary>
    /// Extract all outgoing relationships from a descriptor.
    /// Returns empty list if the descriptor is not the expected concrete type.
    /// Must not mutate state.
    /// </summary>
    IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor);
}
```

- [ ] **Step 4: Create DescriptorRelationshipExtractorBase.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Optional base class for typed relationship extractors.
/// Implements the non-generic IDescriptorRelationshipExtractor with a type-check + cast,
/// then delegates to the typed Extract(TDescriptor) method.
/// AoT-safe: uses standard `is` pattern match, NOT dynamic.
/// </summary>
public abstract class DescriptorRelationshipExtractorBase<TDescriptor>
    : IDescriptorRelationshipExtractor
    where TDescriptor : class, IDescriptor
{
    public abstract DescriptorKind SupportedKind { get; }
    public Type DescriptorType => typeof(TDescriptor);

    public IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor)
    {
        if (descriptor is TDescriptor typed)
            return Extract(typed);
        return Array.Empty<DescriptorRelationship>();
    }

    /// <summary>Typed extraction — override in concrete extractors.</summary>
    protected abstract IReadOnlyList<DescriptorRelationship> Extract(TDescriptor descriptor);
}
```

- [ ] **Step 5: Create IDescriptorRelationshipProvider.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Consumer-facing aggregation API. Dispatches to the correct extractor by concrete descriptor type.
/// Does not trigger registry.Build() or mutate descriptors.
/// </summary>
public interface IDescriptorRelationshipProvider
{
    /// <summary>
    /// Get relationships for this descriptor by finding the extractor whose
    /// DescriptorType matches the descriptor's concrete type.
    /// Returns empty list if no registered extractor matches.
    /// </summary>
    IReadOnlyList<DescriptorRelationship> GetRelationships(IDescriptor descriptor);
}
```

- [ ] **Step 6: Delete IRelationshipAwareDescriptor.cs**

```bash
rm framework/src/CrestCreates.Metadata.Abstractions/IRelationshipAwareDescriptor.cs
```

- [ ] **Step 7: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions
```
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/
git commit -m "feat: add RelationshipStrength, non-generic IDescriptorRelationshipExtractor, IDescriptorRelationshipProvider; extend RelationshipKind; enhance DescriptorRelationship; remove IRelationshipAwareDescriptor"
```

---

### Task 1: Provider Implementation + Schema Extractor + DI Registration

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DefaultDescriptorRelationshipProvider.cs`
- Create: `framework/src/CrestCreates.Metadata/SchemaRelationshipExtractor.cs`
- Modify: `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`

- [ ] **Step 1: Create DefaultDescriptorRelationshipProvider.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DefaultDescriptorRelationshipProvider : IDescriptorRelationshipProvider
{
    private readonly IReadOnlyList<IDescriptorRelationshipExtractor> _extractors;

    public DefaultDescriptorRelationshipProvider(
        IEnumerable<IDescriptorRelationshipExtractor> extractors)
    {
        _extractors = extractors.ToList();
    }

    public IReadOnlyList<DescriptorRelationship> GetRelationships(IDescriptor descriptor)
    {
        foreach (var extractor in _extractors)
        {
            if (extractor.DescriptorType.IsInstanceOfType(descriptor))
                return extractor.Extract(descriptor);
        }
        return Array.Empty<DescriptorRelationship>();
    }
}
```

- [ ] **Step 2: Create SchemaRelationshipExtractor.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Metadata;

public sealed class SchemaRelationshipExtractor
    : DescriptorRelationshipExtractorBase<SchemaDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Schema;

    protected override IReadOnlyList<DescriptorRelationship> Extract(SchemaDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        foreach (var reference in descriptor.References)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("schema", descriptor.Id),
                To: new DescriptorRef("schema", reference.Id, reference.Version),
                Kind: RelationshipKind.References,
                SourcePath: "References",
                Strength: RelationshipStrength.Weak));
        }

        return relationships;
    }
}
```

- [ ] **Step 3: Add AddRelationshipKernel() to MetadataServiceCollectionExtensions.cs**

Edit `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs` — add the new method after `AddBindingStatusKernel()`:

```csharp
public static IServiceCollection AddRelationshipKernel(this IServiceCollection services)
{
    // Provider — aggregates all IDescriptorRelationshipExtractor registrations
    services.TryAddSingleton<IDescriptorRelationshipProvider,
        DefaultDescriptorRelationshipProvider>();

    // Schema extractor — lives in Metadata project, registered here
    services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();

    return services;
}
```

The full file should look like:

```csharp
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Metadata;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddBindingStatusKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRuntimeBindingStatusProvider,
            DefaultDescriptorRuntimeBindingStatusProvider>();
        return services;
    }

    public static IServiceCollection AddRelationshipKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRelationshipProvider,
            DefaultDescriptorRelationshipProvider>();

        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();

        return services;
    }
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DefaultDescriptorRelationshipProvider.cs \
        framework/src/CrestCreates.Metadata/SchemaRelationshipExtractor.cs \
        framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
git commit -m "feat: add DefaultDescriptorRelationshipProvider, SchemaRelationshipExtractor, AddRelationshipKernel()"
```

---

### Task 2: CapabilityDescriptor Cleanup — Remove IRelationshipAwareDescriptor & GetRelationships()

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs`

- [ ] **Step 1: Remove IRelationshipAwareDescriptor from CapabilityDescriptor**

Edit `framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs`:

1. Remove `, IRelationshipAwareDescriptor` from the class declaration line.
2. Remove the entire `GetRelationships()` method body (lines 36-82 in current file) and the `// === IRelationshipAwareDescriptor ===` comment.
3. Remove the `using CrestCreates.Metadata.Abstractions;` if it was only for `IRelationshipAwareDescriptor` (keep it — it's needed for other types).
4. Remove the `using System.Collections.Generic;` if it was only for `IEnumerable<>` in the relationship method.

The class declaration should change from:
```csharp
public sealed class CapabilityDescriptor : IDescriptor, IVersionedDescriptor, IHasContractIdentity, IRelationshipAwareDescriptor
```
to:
```csharp
public sealed class CapabilityDescriptor : IDescriptor, IVersionedDescriptor, IHasContractIdentity
```

The entire `GetRelationships()` method (from `// === IRelationshipAwareDescriptor ===` line through the closing `}`) is removed. The `EventRef` record struct at the bottom of the file is preserved.

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Metadata
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/CapabilityDescriptor.cs
git commit -m "refactor: remove IRelationshipAwareDescriptor and GetRelationships() from CapabilityDescriptor"
```

---

### Task 3: Form Extractor + Remove FormDescriptorDependencyExtractor

**Files:**
- Create: `framework/src/CrestCreates.Form/FormRelationshipExtractor.cs`
- Move: `framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs` → `./99_RecycleBin/FormDescriptorDependencyExtractor.cs`
- Modify: `framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs`

- [ ] **Step 1: Create FormRelationshipExtractor.cs**

```csharp
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Form;

public sealed class FormRelationshipExtractor
    : DescriptorRelationshipExtractorBase<FormDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Form;

    protected override IReadOnlyList<DescriptorRelationship> Extract(FormDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>
        {
            new(
                From: new DescriptorRef("form", descriptor.Id),
                To: new DescriptorRef("schema", descriptor.Schema.Id, descriptor.Schema.Version),
                Kind: RelationshipKind.Uses,
                Role: "Schema",
                SourcePath: "Schema",
                Strength: RelationshipStrength.Strong)
        };

        return relationships;
    }
}
```

- [ ] **Step 2: Move FormDescriptorDependencyExtractor to recycle bin**

```bash
mkdir -p ./99_RecycleBin
mv framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs ./99_RecycleBin/
```

- [ ] **Step 3: Register FormRelationshipExtractor in FormServiceCollectionExtensions.cs**

Add after the `FormBindingStatusContributor` registration line in `AddFormKernel()`:

```csharp
// Relationship Extractor
services.AddSingleton<IDescriptorRelationshipExtractor, FormRelationshipExtractor>();
```

Full `AddFormKernel()` should be:

```csharp
public static IServiceCollection AddFormKernel(this IServiceCollection services)
{
    services.TryAddSingleton<IFormRegistry, FormRegistry>();
    services.TryAddSingleton<IRegistryValidationEngine<FormDescriptor>,
        RegistryValidationEngine<FormDescriptor>>();
    services.TryAddSingleton<IRegistryValidator<FormDescriptor>,
        FormDescriptorValidator>();
    services.TryAddSingleton<FormSchemaBindingValidator>();
    services.AddSingleton<IDescriptorBindingStatusContributor, FormBindingStatusContributor>();

    // Relationship Extractor
    services.AddSingleton<IDescriptorRelationshipExtractor, FormRelationshipExtractor>();

    return services;
}
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Form
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Form/FormRelationshipExtractor.cs \
        framework/src/CrestCreates.Form/FormServiceCollectionExtensions.cs \
        ./99_RecycleBin/FormDescriptorDependencyExtractor.cs
git rm framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs 2>/dev/null; true
git commit -m "feat: add FormRelationshipExtractor; remove FormDescriptorDependencyExtractor → recycle bin"
```

---

### Task 4: Capability Relationship Extractor

**Files:**
- Create: `framework/src/CrestCreates.Capability/CapabilityRelationshipExtractor.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Create CapabilityRelationshipExtractor.cs**

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityRelationshipExtractor
    : DescriptorRelationshipExtractorBase<CapabilityDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Capability;

    protected override IReadOnlyList<DescriptorRelationship> Extract(CapabilityDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // InputSchema → SchemaDescriptor (Consumes, Strong)
        if (descriptor.InputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id),
                To: new DescriptorRef("schema", descriptor.InputSchema.Value.Id, descriptor.InputSchema.Value.Version),
                Kind: RelationshipKind.Consumes,
                Role: "InputSchema",
                SourcePath: "InputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // OutputSchema → SchemaDescriptor (Produces, Strong)
        if (descriptor.OutputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id),
                To: new DescriptorRef("schema", descriptor.OutputSchema.Value.Id, descriptor.OutputSchema.Value.Version),
                Kind: RelationshipKind.Produces,
                Role: "OutputSchema",
                SourcePath: "OutputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // Produces[] → Event descriptors (Produces, Weak)
        foreach (var @event in descriptor.Produces)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id),
                To: new DescriptorRef(@event.Namespace, @event.Id, @event.Version),
                Kind: RelationshipKind.Produces,
                SourcePath: "Produces",
                Strength: RelationshipStrength.Weak));
        }

        // Consumes[] → Event descriptors (Consumes, Weak)
        foreach (var @event in descriptor.Consumes)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id),
                To: new DescriptorRef(@event.Namespace, @event.Id, @event.Version),
                Kind: RelationshipKind.Consumes,
                SourcePath: "Consumes",
                Strength: RelationshipStrength.Weak));
        }

        // SupersededById → CapabilityDescriptor (DependsOn, Weak)
        if (descriptor.SupersededById is not null)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("capability", descriptor.Id),
                To: new DescriptorRef("capability", descriptor.SupersededById),
                Kind: RelationshipKind.DependsOn,
                Role: "SupersededBy",
                SourcePath: "SupersededById",
                Strength: RelationshipStrength.Weak));
        }

        return relationships;
    }
}
```

- [ ] **Step 2: Register CapabilityRelationshipExtractor in CapabilityServiceCollectionExtensions.cs**

Add after the `CapabilityBindingStatusContributor` registration in `AddCapabilityRuntime()`:

```csharp
// Relationship Extractor
services.AddSingleton<IDescriptorRelationshipExtractor, CapabilityRelationshipExtractor>();
```

This goes right after line `services.AddSingleton<IDescriptorBindingStatusContributor, CapabilityBindingStatusContributor>();` and before the `return services;`.

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Capability
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Capability/CapabilityRelationshipExtractor.cs \
        framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs
git commit -m "feat: add CapabilityRelationshipExtractor with correct schema namespace"
```

---

### Task 5: Event Relationship Extractor

**Files:**
- Create: `framework/src/CrestCreates.Event/EventRelationshipExtractor.cs`
- Modify: `framework/src/CrestCreates.Event/EventServiceCollectionExtensions.cs`

- [ ] **Step 1: Create EventRelationshipExtractor.cs**

```csharp
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Event;

public sealed class EventRelationshipExtractor
    : DescriptorRelationshipExtractorBase<GeneratedEventDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Event;

    protected override IReadOnlyList<DescriptorRelationship> Extract(GeneratedEventDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // PayloadSchemaRef → SchemaDescriptor (Uses, Strong)
        // PayloadSchemaRef is a VersionedDescriptorRef<SchemaDescriptor> record struct — never null.
        // Emit relationship even if Id is empty (structural validation is validator's job).
        relationships.Add(new DescriptorRelationship(
            From: new DescriptorRef("event", descriptor.Id),
            To: new DescriptorRef("schema", descriptor.PayloadSchemaRef.Id, descriptor.PayloadSchemaRef.Version),
            Kind: RelationshipKind.Uses,
            Role: "PayloadSchema",
            SourcePath: "PayloadSchemaRef",
            Strength: RelationshipStrength.Strong));

        return relationships;
    }
}
```

- [ ] **Step 2: Register EventRelationshipExtractor in EventServiceCollectionExtensions.cs**

Add after the `EventBindingStatusContributor` registration in `AddEventKernel()`:

```csharp
// Relationship Extractor
services.AddSingleton<IDescriptorRelationshipExtractor, EventRelationshipExtractor>();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Event
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Event/EventRelationshipExtractor.cs \
        framework/src/CrestCreates.Event/EventServiceCollectionExtensions.cs
git commit -m "feat: add EventRelationshipExtractor for GeneratedEventDescriptor"
```

---

### Task 6: HumanTask Relationship Extractor

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/HumanTaskRelationshipExtractor.cs`
- Modify: `framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs`

- [ ] **Step 1: Create HumanTaskRelationshipExtractor.cs**

```csharp
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskRelationshipExtractor
    : DescriptorRelationshipExtractorBase<HumanTaskDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.HumanTask;

    protected override IReadOnlyList<DescriptorRelationship> Extract(HumanTaskDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // Interaction → FormDescriptor (Uses, Strong)
        // Interaction is VersionedDescriptorRef<IInteractionDescriptor> — record struct, never null
        relationships.Add(new DescriptorRelationship(
            From: new DescriptorRef("humantask", descriptor.Id),
            To: new DescriptorRef("form", descriptor.Interaction.Id, descriptor.Interaction.Version),
            Kind: RelationshipKind.Uses,
            Role: "Interaction",
            SourcePath: "Interaction",
            Strength: RelationshipStrength.Strong));

        // InputSchema → SchemaDescriptor (Consumes, Strong)
        if (descriptor.InputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("humantask", descriptor.Id),
                To: new DescriptorRef("schema", descriptor.InputSchema.Value.Id, descriptor.InputSchema.Value.Version),
                Kind: RelationshipKind.Consumes,
                Role: "InputSchema",
                SourcePath: "InputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // OutputSchema → SchemaDescriptor (Produces, Strong)
        if (descriptor.OutputSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("humantask", descriptor.Id),
                To: new DescriptorRef("schema", descriptor.OutputSchema.Value.Id, descriptor.OutputSchema.Value.Version),
                Kind: RelationshipKind.Produces,
                Role: "OutputSchema",
                SourcePath: "OutputSchema",
                Strength: RelationshipStrength.Strong));
        }

        // Outcomes[].Capability → CapabilityDescriptor (Triggers, Strong)
        foreach (var outcome in descriptor.Outcomes)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("humantask", descriptor.Id),
                To: new DescriptorRef("capability", outcome.Capability.Id, outcome.Capability.Version),
                Kind: RelationshipKind.Triggers,
                Role: "Outcome",
                SourcePath: "Outcomes",
                Strength: RelationshipStrength.Strong));
        }

        return relationships;
    }
}
```

- [ ] **Step 2: Register HumanTaskRelationshipExtractor in HumanTaskServiceCollectionExtensions.cs**

Add after the `HumanTaskBindingStatusContributor` registration in `AddHumanTaskRuntime()`:

```csharp
// Relationship Extractor
services.AddSingleton<IDescriptorRelationshipExtractor, HumanTaskRelationshipExtractor>();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/src/CrestCreates.HumanTask
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/HumanTaskRelationshipExtractor.cs \
        framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs
git commit -m "feat: add HumanTaskRelationshipExtractor"
```

---

### Task 7: Workflow Relationship Extractor

**Files:**
- Create: `framework/src/CrestCreates.Workflow/WorkflowRelationshipExtractor.cs`
- Modify: `framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs`

- [ ] **Step 1: Create WorkflowRelationshipExtractor.cs**

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowRelationshipExtractor
    : DescriptorRelationshipExtractorBase<WorkflowDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.Workflow;

    protected override IReadOnlyList<DescriptorRelationship> Extract(WorkflowDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // VariableSchema → SchemaDescriptor (Uses, Strong) — nullable
        if (descriptor.VariableSchema.HasValue)
        {
            relationships.Add(new DescriptorRelationship(
                From: new DescriptorRef("workflow", descriptor.Id),
                To: new DescriptorRef("schema", descriptor.VariableSchema.Value.Id, descriptor.VariableSchema.Value.Version),
                Kind: RelationshipKind.Uses,
                Role: "VariableSchema",
                SourcePath: "VariableSchema",
                Strength: RelationshipStrength.Strong));
        }

        // Step targets
        foreach (var step in descriptor.Steps)
        {
            switch (step.Target)
            {
                case CapabilityTarget capabilityTarget:
                    relationships.Add(new DescriptorRelationship(
                        From: new DescriptorRef("workflow", descriptor.Id),
                        To: new DescriptorRef("capability", capabilityTarget.Capability.Id, capabilityTarget.Capability.Version),
                        Kind: RelationshipKind.Triggers,
                        Role: "CapabilityStep",
                        SourcePath: "Steps",
                        Strength: RelationshipStrength.Strong));
                    break;

                case HumanTaskTarget humanTaskTarget:
                    relationships.Add(new DescriptorRelationship(
                        From: new DescriptorRef("workflow", descriptor.Id),
                        To: new DescriptorRef("humantask", humanTaskTarget.HumanTask.Id, humanTaskTarget.HumanTask.Version),
                        Kind: RelationshipKind.Triggers,
                        Role: "HumanTaskStep",
                        SourcePath: "Steps",
                        Strength: RelationshipStrength.Strong));
                    break;

                case SubWorkflowTarget subWorkflowTarget:
                    // Emitted as Weak + IsRuntimeBinding=false — does NOT imply runtime support
                    relationships.Add(new DescriptorRelationship(
                        From: new DescriptorRef("workflow", descriptor.Id),
                        To: new DescriptorRef("workflow", subWorkflowTarget.Workflow.Id, subWorkflowTarget.Workflow.Version),
                        Kind: RelationshipKind.References,
                        Role: "SubWorkflowStep",
                        SourcePath: "Steps",
                        Strength: RelationshipStrength.Weak,
                        IsRuntimeBinding: false));
                    break;
            }
        }

        return relationships;
    }
}
```

- [ ] **Step 2: Register WorkflowRelationshipExtractor in WorkflowServiceCollectionExtensions.cs**

Add after the `WorkflowBindingStatusContributor` registration in `AddWorkflowEngine()`:

```csharp
// Relationship Extractor
services.AddSingleton<IDescriptorRelationshipExtractor, WorkflowRelationshipExtractor>();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Workflow
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowRelationshipExtractor.cs \
        framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs
git commit -m "feat: add WorkflowRelationshipExtractor with SubWorkflowTarget Weak/IsRuntimeBinding=false"
```

---

### Task 8: Tests — Core Types, Provider, Schema Extractor

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/RelationshipStrengthTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/RelationshipKindExtensionTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorRelationshipEnhancementTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/SchemaRelationshipExtractorTests.cs`
- Create: `framework/test/CrestCreates.Metadata.Tests/DefaultDescriptorRelationshipProviderTests.cs`

- [ ] **Step 1: Create RelationshipStrengthTests.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class RelationshipStrengthTests
{
    [Fact]
    public void RelationshipStrength_Has_Strong_And_Weak_Values()
    {
        Enum.GetValues<RelationshipStrength>().Should().Contain(RelationshipStrength.Strong);
        Enum.GetValues<RelationshipStrength>().Should().Contain(RelationshipStrength.Weak);
    }

    [Fact]
    public void RelationshipStrength_Defaults_To_Strong()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References);

        rel.Strength.Should().Be(RelationshipStrength.Strong);
    }
}
```

- [ ] **Step 2: Create RelationshipKindExtensionTests.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class RelationshipKindExtensionTests
{
    [Fact]
    public void RelationshipKind_Includes_Uses()
    {
        Enum.GetValues<RelationshipKind>().Should().Contain(RelationshipKind.Uses);
    }

    [Fact]
    public void RelationshipKind_Includes_Triggers()
    {
        Enum.GetValues<RelationshipKind>().Should().Contain(RelationshipKind.Triggers);
    }

    [Fact]
    public void RelationshipKind_Has_Six_Values()
    {
        Enum.GetValues<RelationshipKind>().Should().HaveCount(6);
    }
}
```

- [ ] **Step 3: Create DescriptorRelationshipEnhancementTests.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRelationshipEnhancementTests
{
    [Fact]
    public void DescriptorRelationship_Has_Role_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            Role: "InputSchema");

        rel.Role.Should().Be("InputSchema");
    }

    [Fact]
    public void DescriptorRelationship_Has_SourcePath_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            SourcePath: "InputSchema");

        rel.SourcePath.Should().Be("InputSchema");
    }

    [Fact]
    public void DescriptorRelationship_Has_Strength_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            Strength: RelationshipStrength.Weak);

        rel.Strength.Should().Be(RelationshipStrength.Weak);
    }

    [Fact]
    public void DescriptorRelationship_Has_IsRuntimeBinding_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            IsRuntimeBinding: true);

        rel.IsRuntimeBinding.Should().BeTrue();
    }

    [Fact]
    public void DescriptorRelationship_IsRuntimeBinding_Defaults_To_False()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References);

        rel.IsRuntimeBinding.Should().BeFalse();
    }

    [Fact]
    public void DescriptorRelationship_Role_Defaults_To_Null()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References);

        rel.Role.Should().BeNull();
    }
}
```

- [ ] **Step 4: Create SchemaRelationshipExtractorTests.cs**

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class SchemaRelationshipExtractorTests
{
    private readonly SchemaRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_References_Relationships()
    {
        var schema = new SchemaDescriptor
        {
            Id = "order",
            Name = "Order",
            Version = 1,
            References = new[]
            {
                new VersionedDescriptorRef<SchemaDescriptor> { Id = "customer", Version = 2 },
                new VersionedDescriptorRef<SchemaDescriptor> { Id = "product", Version = 1 }
            }
        };

        var relationships = _extractor.Extract(schema);

        relationships.Should().HaveCount(2);
        relationships.Should().AllSatisfy(r =>
        {
            r.Kind.Should().Be(RelationshipKind.References);
            r.From.Namespace.Should().Be("schema");
            r.From.Id.Should().Be("order");
            r.To.Namespace.Should().Be("schema");
            r.SourcePath.Should().Be("References");
            r.Strength.Should().Be(RelationshipStrength.Weak);
        });
        relationships[0].To.Id.Should().Be("customer");
        relationships[1].To.Id.Should().Be("product");
    }

    [Fact]
    public void Extract_Returns_Empty_When_No_References()
    {
        var schema = new SchemaDescriptor
        {
            Id = "order",
            Name = "Order",
            Version = 1,
            References = Array.Empty<VersionedDescriptorRef<SchemaDescriptor>>()
        };

        var relationships = _extractor.Extract(schema);

        relationships.Should().BeEmpty();
    }

    [Fact]
    public void SupportedKind_Is_Schema()
    {
        _extractor.SupportedKind.Should().Be(DescriptorKind.Schema);
    }

    [Fact]
    public void DescriptorType_Is_SchemaDescriptor()
    {
        _extractor.DescriptorType.Should().Be(typeof(SchemaDescriptor));
    }
}
```

- [ ] **Step 5: Create DefaultDescriptorRelationshipProviderTests.cs**

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DefaultDescriptorRelationshipProviderTests
{
    [Fact]
    public void GetRelationships_Dispatches_To_Correct_Concrete_Type()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();
        services.AddSingleton<IDescriptorRelationshipProvider, DefaultDescriptorRelationshipProvider>();
        var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDescriptorRelationshipProvider>();
        var schema = new Schema.Abstractions.SchemaDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            References = new[]
            {
                new VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor> { Id = "ref1", Version = 1 }
            }
        };

        var relationships = provider.GetRelationships(schema);

        relationships.Should().HaveCount(1);
        relationships[0].To.Id.Should().Be("ref1");
    }

    [Fact]
    public void GetRelationships_Returns_Empty_For_Unknown_Concrete_Type()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();
        services.AddSingleton<IDescriptorRelationshipProvider, DefaultDescriptorRelationshipProvider>();
        var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDescriptorRelationshipProvider>();

        // A descriptor type with no registered extractor
        var unknownDescriptor = new UnknownDescriptor();

        var relationships = provider.GetRelationships(unknownDescriptor);

        relationships.Should().BeEmpty();
    }

    private sealed class UnknownDescriptor : IDescriptor
    {
        public string Namespace => "unknown";
        public string Id => "x";
        public string Name => "Unknown";
        public DescriptorKind Kind => (DescriptorKind)999;
        public DescriptorState State => DescriptorState.Active;
        public string ContractHash => "";
        public string DefinitionHash => "";
        public string? SupersededById => null;
    }
}
```

- [ ] **Step 6: Run Metadata tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~RelationshipStrengthTests|FullyQualifiedName~RelationshipKindExtensionTests|FullyQualifiedName~DescriptorRelationshipEnhancementTests|FullyQualifiedName~SchemaRelationshipExtractorTests|FullyQualifiedName~DefaultDescriptorRelationshipProviderTests" -v
```
Expected: all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/
git commit -m "test: add core type, provider, and Schema extractor tests"
```

---

### Task 9: Tests — Per-Extractor Tests

**Files:**
- Create: `framework/test/CrestCreates.Form.Tests/FormRelationshipExtractorTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityRelationshipExtractorTests.cs`
- Create: `framework/test/CrestCreates.Event.Tests/EventRelationshipExtractorTests.cs`
- Create: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRelationshipExtractorTests.cs`
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowRelationshipExtractorTests.cs`

- [ ] **Step 1: Create FormRelationshipExtractorTests.cs**

```csharp
using CrestCreates.Form;
using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Form.Tests;

public class FormRelationshipExtractorTests
{
    private readonly FormRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_Schema_Relationship()
    {
        var form = new FormDescriptor
        {
            Id = "order-form",
            Name = "Order Form",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-schema", Version = 2 }
        };

        var relationships = _extractor.Extract(form);

        relationships.Should().HaveCount(1);
        var rel = relationships[0];
        rel.From.Namespace.Should().Be("form");
        rel.From.Id.Should().Be("order-form");
        rel.To.Namespace.Should().Be("schema");
        rel.To.Id.Should().Be("order-schema");
        rel.Kind.Should().Be(RelationshipKind.Uses);
        rel.Role.Should().Be("Schema");
        rel.SourcePath.Should().Be("Schema");
        rel.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Extract_Emits_Even_When_Schema_Id_Empty()
    {
        // VersionedDescriptorRef<T> is a record struct — never null.
        // Extractor emits relationship as-is; structural validation is validator's job.
        var form = new FormDescriptor
        {
            Id = "order-form",
            Name = "Order Form",
            Version = 1,
            Schema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "", Version = 0 }
        };

        var relationships = _extractor.Extract(form);

        relationships.Should().HaveCount(1);
        relationships[0].To.Id.Should().Be("");
    }

    [Fact]
    public void SupportedKind_Is_Form()
    {
        _extractor.SupportedKind.Should().Be(DescriptorKind.Form);
    }
}
```

- [ ] **Step 2: Run Form tests**

```bash
dotnet test framework/test/CrestCreates.Form.Tests --filter "FullyQualifiedName~FormRelationshipExtractorTests" -v
```
Expected: 3 tests PASS.

- [ ] **Step 3: Create CapabilityRelationshipExtractorTests.cs**

```csharp
using CrestCreates.Capability;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityRelationshipExtractorTests
{
    private readonly CapabilityRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Full_Capability_Returns_All_Relationships()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "approve-order",
            Name = "Approve Order",
            Version = 1,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-input", Version = 1 },
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-output", Version = 1 },
            Produces = new[] { new EventRef("event", "order.approved", 1) },
            Consumes = new[] { new EventRef("event", "order.submitted", 1) },
            SupersededById = "approve-order-v2"
        };

        var relationships = _extractor.Extract(capability);

        relationships.Should().HaveCount(5);
    }

    [Fact]
    public void Extract_Schema_Refs_Use_Correct_Schema_Namespace()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "test-input", Version = 1 }
        };

        var relationships = _extractor.Extract(capability);

        var schemaRel = relationships.Should().ContainSingle(r => r.Role == "InputSchema").Subject;
        schemaRel.To.Namespace.Should().Be("schema", "schema refs must use the 'schema' namespace, not the schema's Id");
        schemaRel.To.Id.Should().Be("test-input");
    }

    [Fact]
    public void Extract_Nullable_InputSchema_Omitted()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            InputSchema = null,
            OutputSchema = null
        };

        var relationships = _extractor.Extract(capability);

        relationships.Should().BeEmpty();
    }

    [Fact]
    public void Extract_Nullable_OutputSchema_Omitted()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            InputSchema = null,
            OutputSchema = null
        };

        var relationships = _extractor.Extract(capability);

        relationships.Should().NotContain(r => r.Role == "InputSchema" || r.Role == "OutputSchema");
    }

    [Fact]
    public void Extract_Event_Produces_Weak_Strength()
    {
        var capability = new CapabilityDescriptor
        {
            Id = "test",
            Name = "Test",
            Version = 1,
            Produces = new[] { new EventRef("event", "test.event", 1) }
        };

        var relationships = _extractor.Extract(capability);

        var eventRel = relationships.Should().ContainSingle(r => r.Kind == RelationshipKind.Produces && r.SourcePath == "Produces").Subject;
        eventRel.Strength.Should().Be(RelationshipStrength.Weak);
    }
}
```

- [ ] **Step 4: Run Capability tests**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests --filter "FullyQualifiedName~CapabilityRelationshipExtractorTests" -v
```
Expected: 5 tests PASS.

- [ ] **Step 5: Create EventRelationshipExtractorTests.cs**

```csharp
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Event.Tests;

public class EventRelationshipExtractorTests
{
    private readonly EventRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_PayloadSchemaRef_Relationship()
    {
        var descriptor = CreateEventDescriptor("order-approved", "order-schema", 2);

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(1);
        var rel = relationships[0];
        rel.From.Namespace.Should().Be("event");
        rel.From.Id.Should().Be("order-approved");
        rel.To.Namespace.Should().Be("schema");
        rel.To.Id.Should().Be("order-schema");
        rel.Kind.Should().Be(RelationshipKind.Uses);
        rel.Role.Should().Be("PayloadSchema");
        rel.SourcePath.Should().Be("PayloadSchemaRef");
        rel.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Extract_Emits_Even_When_PayloadSchemaRef_Id_Empty()
    {
        // VersionedDescriptorRef<T> is a record struct — never null.
        var descriptor = CreateEventDescriptor("order-approved", "", 0);

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(1);
        relationships[0].To.Id.Should().Be("");
    }

    [Fact]
    public void SupportedKind_Is_Event()
    {
        _extractor.SupportedKind.Should().Be(DescriptorKind.Event);
    }

    [Fact]
    public void DescriptorType_Is_GeneratedEventDescriptor()
    {
        _extractor.DescriptorType.Should().Be(typeof(GeneratedEventDescriptor));
    }

    private static GeneratedEventDescriptor CreateEventDescriptor(string id, string schemaId, int schemaVersion)
    {
        return new GeneratedEventDescriptor
        {
            Id = id,
            Name = "Test Event",
            Version = 1,
            PayloadType = typeof(string),
            PayloadSchemaRef = new VersionedDescriptorRef<SchemaDescriptor>
            {
                Id = schemaId,
                Version = schemaVersion
            }
        };
    }
}
```

- [ ] **Step 6: Run Event tests**

```bash
dotnet test framework/test/CrestCreates.Event.Tests --filter "FullyQualifiedName~EventRelationshipExtractorTests" -v
```
Expected: 4 tests PASS.

- [ ] **Step 7: Create HumanTaskRelationshipExtractorTests.cs**

```csharp
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRelationshipExtractorTests
{
    private readonly HumanTaskRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_All_Four_Ref_Types()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "review-input", Version = 1 },
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "review-output", Version = 1 },
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Name = "Approved",
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor> { Id = "approve-order", Version = 1 }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(4);
    }

    [Fact]
    public void Extract_Interaction_Is_Uses_Kind()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            Outcomes = Array.Empty<CompletionOutcome>()
        };

        var relationships = _extractor.Extract(descriptor);

        var interaction = relationships.Should().ContainSingle(r => r.Role == "Interaction").Subject;
        interaction.Kind.Should().Be(RelationshipKind.Uses);
        interaction.To.Namespace.Should().Be("form");
        interaction.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Extract_Outcome_Capability_Is_Triggers_Kind()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            Outcomes = new[]
            {
                new CompletionOutcome
                {
                    Name = "Approved",
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor> { Id = "approve-order", Version = 1 }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var outcome = relationships.Should().ContainSingle(r => r.Role == "Outcome").Subject;
        outcome.Kind.Should().Be(RelationshipKind.Triggers);
        outcome.To.Namespace.Should().Be("capability");
    }

    [Fact]
    public void Extract_Nullable_Schemas_Omitted()
    {
        var descriptor = new HumanTaskDescriptor
        {
            Id = "review-order",
            Name = "Review Order",
            Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor> { Id = "review-form", Version = 1 },
            InputSchema = null,
            OutputSchema = null,
            Outcomes = Array.Empty<CompletionOutcome>()
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(1); // only Interaction
        relationships.Should().NotContain(r => r.Role == "InputSchema" || r.Role == "OutputSchema");
    }
}
```

- [ ] **Step 8: Run HumanTask tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~HumanTaskRelationshipExtractorTests" -v
```
Expected: 4 tests PASS.

- [ ] **Step 9: Create WorkflowRelationshipExtractorTests.cs**

```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowRelationshipExtractorTests
{
    private readonly WorkflowRelationshipExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_VariableSchema_And_StepTargets()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "order-workflow",
            Name = "Order Workflow",
            Version = 1,
            VariableSchema = new VersionedDescriptorRef<SchemaDescriptor> { Id = "order-vars", Version = 1 },
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor> { Id = "validate-order", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(2);
        relationships.Should().ContainSingle(r => r.Role == "VariableSchema" && r.Kind == RelationshipKind.Uses);
        relationships.Should().ContainSingle(r => r.Role == "CapabilityStep" && r.Kind == RelationshipKind.Triggers);
    }

    [Fact]
    public void Extract_Nullable_VariableSchema_Omitted()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "order-workflow",
            Name = "Order Workflow",
            Version = 1,
            VariableSchema = null,
            Steps = Array.Empty<WorkflowStep>()
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().BeEmpty();
    }

    [Fact]
    public void Extract_CapabilityStep_Is_Strong_Triggers()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "w1",
            Name = "W1",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor> { Id = "c1", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var capRel = relationships.Should().ContainSingle(r => r.Role == "CapabilityStep").Subject;
        capRel.Kind.Should().Be(RelationshipKind.Triggers);
        capRel.Strength.Should().Be(RelationshipStrength.Strong);
        capRel.To.Namespace.Should().Be("capability");
    }

    [Fact]
    public void Extract_HumanTaskStep_Is_Strong_Triggers()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "w1",
            Name = "W1",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTask.Abstractions.HumanTaskDescriptor> { Id = "ht1", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var htRel = relationships.Should().ContainSingle(r => r.Role == "HumanTaskStep").Subject;
        htRel.Kind.Should().Be(RelationshipKind.Triggers);
        htRel.Strength.Should().Be(RelationshipStrength.Strong);
        htRel.To.Namespace.Should().Be("humantask");
    }

    [Fact]
    public void Extract_SubWorkflowTarget_Is_Weak_NotRuntimeBinding()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "w1",
            Name = "W1",
            Version = 1,
            Steps = new[]
            {
                new WorkflowStep
                {
                    Id = "step1",
                    Target = new SubWorkflowTarget
                    {
                        Workflow = new VersionedDescriptorRef<WorkflowDescriptor> { Id = "w2", Version = 1 }
                    }
                }
            }
        };

        var relationships = _extractor.Extract(descriptor);

        var subRel = relationships.Should().ContainSingle(r => r.Role == "SubWorkflowStep").Subject;
        subRel.Kind.Should().Be(RelationshipKind.References);
        subRel.Strength.Should().Be(RelationshipStrength.Weak);
        subRel.IsRuntimeBinding.Should().BeFalse();
    }
}
```

- [ ] **Step 10: Run Workflow tests**

```bash
dotnet test framework/test/CrestCreates.Workflow.Tests --filter "FullyQualifiedName~WorkflowRelationshipExtractorTests" -v
```
Expected: 5 tests PASS.

- [ ] **Step 11: Commit**

```bash
git add framework/test/CrestCreates.Form.Tests/FormRelationshipExtractorTests.cs \
        framework/test/CrestCreates.Capability.Tests/CapabilityRelationshipExtractorTests.cs \
        framework/test/CrestCreates.Event.Tests/EventRelationshipExtractorTests.cs \
        framework/test/CrestCreates.HumanTask.Tests/HumanTaskRelationshipExtractorTests.cs \
        framework/test/CrestCreates.Workflow.Tests/WorkflowRelationshipExtractorTests.cs
git commit -m "test: add per-extractor relationship tests for Form, Capability, Event, HumanTask, Workflow"
```

---

### Task 10: Regression Gate — Full Build & All Tests

- [ ] **Step 1: Full solution build**

```bash
dotnet build
```
Expected: 0 errors.

- [ ] **Step 2: Run all existing test suites to verify zero regressions**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests -v
dotnet test framework/test/CrestCreates.Form.Tests -v
dotnet test framework/test/CrestCreates.Capability.Tests -v
dotnet test framework/test/CrestCreates.Event.Tests -v
dotnet test framework/test/CrestCreates.HumanTask.Tests -v
dotnet test framework/test/CrestCreates.Workflow.Tests -v
```
Expected: ALL existing tests PASS (zero regressions).

- [ ] **Step 3: Verify IRelationshipAwareDescriptor is removed**

```bash
! grep -r "IRelationshipAwareDescriptor" framework/src/ --include="*.cs"
```
Expected: no output (no file references the removed interface).

- [ ] **Step 4: Verify FormDescriptorDependencyExtractor is removed**

```bash
! test -f framework/src/CrestCreates.Form/FormDescriptorDependencyExtractor.cs
```
Expected: file does not exist (moved to recycle bin).

- [ ] **Step 5: Commit (if any changes)**

```bash
git add -A
git commit -m "chore: regression gate — full build + all tests passing"
```

---

## Self-Review Checklist

Before declaring this plan complete, verify:

1. **Spec coverage**: Each section of the spec maps to a task above:
   - §3.1-3.3 (RelationshipKind, Strength, DescriptorRelationship) → Task 0
   - §3.4-3.7 (IDescriptorRelationshipExtractor, Base, Provider, DefaultProvider) → Task 0 + Task 1
   - §4.1 (Schema extractor) → Task 1
   - §4.2 (Form extractor) → Task 3
   - §4.3 (Capability extractor) → Task 4
   - §4.4 (Event extractor) → Task 5
   - §4.5 (HumanTask extractor) → Task 6
   - §4.6 (Workflow extractor) → Task 7
   - §8.1-8.3 (Code removal) → Task 0 + Task 2 + Task 3
   - §10 (DI registration) → Tasks 1,3,4,5,6,7
   - §11 (Testing) → Tasks 8-9
   - §12 (Regression) → Task 10

2. **Type consistency**:
   - `IDescriptorRelationshipExtractor` (non-generic) used consistently in DI registrations
   - `DescriptorRelationshipExtractorBase<TDescriptor>` used consistently in all extractors
   - `CapabilityDescriptor` no longer implements `IRelationshipAwareDescriptor`
   - All extractors registered in their respective `*ServiceCollectionExtensions`
   - `SchemaRelationshipExtractor` registered in `MetadataServiceCollectionExtensions` (same project)

3. **No placeholders**: All steps contain complete code, exact commands, expected output.
