# Phase 11: Build-Time Descriptor Ref Validation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Validate at compile-time that all `VersionedDescriptorRef<T>` references resolve to registered descriptors. Catch broken refs (deleted capability, moved schema, renamed event) before runtime — eliminating a class of runtime errors.

**Architecture:** A new Roslyn `IIncrementalGenerator` runs after the descriptor registries are populated. It collects all `VersionedDescriptorRef` usages across the compilation, resolves them against the generated registry code, and emits build errors for unresolved refs. The validator is driven by the generated registry module initializers — it reads the set of registered descriptor IDs from the generated code, then scans the compilation for refs that don't match.

**Tech Stack:** Roslyn IIncrementalGenerator (netstandard2.0), .NET 10, xUnit

---

### Task 0: RefValidationSourceGenerator — Core Logic

**Files:**
- Create: `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/RefValidationSourceGenerator.cs`
- Modify: `framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj` (no changes needed, already targets netstandard2.0 with Roslyn)

The generator collects all registered descriptor IDs from generated registry initializers, then scans the compilation for `VersionedDescriptorRef<T>` constructions with unresolvable IDs.

- [ ] **Step 1: Write RefValidationSourceGenerator.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CrestCreates.CodeGenerator.SchemaCapabilityGenerator;

[Generator]
public sealed class RefValidationSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var refs = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsVersionedDescriptorRef(node),
                transform: static (ctx, ct) => ExtractRef(ctx))
            .Where(static x => x is not null)
            .Collect();

        var compilationProvider = context.CompilationProvider;

        context.RegisterSourceOutput(
            refs.Combine(compilationProvider),
            static (spc, source) =>
            {
                ValidateReferences(spc, source.Left, source.Right);
            });
    }

    private static bool IsVersionedDescriptorRef(SyntaxNode node)
    {
        return node is ObjectCreationExpressionSyntax creation
            && creation.Type is GenericNameSyntax generic
            && generic.Identifier.Text == "VersionedDescriptorRef";
    }

    private static DescriptorRefInfo? ExtractRef(GeneratorSyntaxContext ctx)
    {
        var creation = (ObjectCreationExpressionSyntax)ctx.Node;
        var generic = (GenericNameSyntax)creation.Type;

        // Extract Id argument (first constructor argument)
        var idArg = creation.ArgumentList?.Arguments.FirstOrDefault();
        if (idArg == null) return null;

        var idValue = ctx.SemanticModel.GetConstantValue(idArg.Expression);
        if (!idValue.HasValue || idValue.Value is not string id) return null;

        // Extract Version if available (second argument)
        var versionArg = creation.ArgumentList!.Arguments.Skip(1).FirstOrDefault();
        int? version = null;
        if (versionArg != null)
        {
            var v = ctx.SemanticModel.GetConstantValue(versionArg.Expression);
            if (v.HasValue && v.Value is int ver) version = ver;
        }

        // Get descriptor type name
        var typeArg = generic.TypeArgumentList.Arguments.FirstOrDefault();

        return new DescriptorRefInfo
        {
            Id = id,
            Version = version,
            DescriptorType = typeArg?.ToString() ?? "Unknown",
            Location = creation.GetLocation()
        };
    }

    private static void ValidateReferences(
        SourceProductionContext spc,
        ImmutableArray<DescriptorRefInfo?> refs,
        Compilation compilation)
    {
        var validRefs = refs.Where(r => r != null).Select(r => r!).ToList();
        if (validRefs.Count == 0) return;

        // Collect all known descriptor IDs from this compilation's generated registries.
        // Descriptor IDs follow conventions: schema_*, cap_*, evt_*, form_*, ht_*, wf_*
        // We scan for string literals matching these patterns in the compilation.
        var knownIds = CollectKnownDescriptorIds(compilation);

        foreach (var descriptorRef in validRefs)
        {
            if (!knownIds.Contains(descriptorRef.Id))
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "CC1001",
                        "Unresolved descriptor reference",
                        "VersionedDescriptorRef<{0}> references descriptor '{1}' which is not registered in any registry. Ensure the descriptor exists and is registered via a DescriptorProvider or source generator.",
                        "DescriptorValidation",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    descriptorRef.Location,
                    descriptorRef.DescriptorType,
                    descriptorRef.Id);

                spc.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static HashSet<string> CollectKnownDescriptorIds(Compilation compilation)
    {
        var ids = new HashSet<string>();

        // Scan all syntax trees for generated registry code that registers descriptors.
        // Patterns: schema_*, cap_*, evt_*, form_*, ht_*, wf_* followed by hex chars
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var strings = root.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression));

            foreach (var str in strings)
            {
                var value = str.Token.ValueText;
                if (IsDescriptorId(value))
                    ids.Add(value);
            }
        }

        return ids;
    }

    private static bool IsDescriptorId(string value)
    {
        return value.StartsWith("schema_")
            || value.StartsWith("cap_")
            || value.StartsWith("evt_")
            || value.StartsWith("form_")
            || value.StartsWith("ht_")
            || value.StartsWith("wf_");
    }
}

internal sealed class DescriptorRefInfo
{
    public string Id { get; set; } = string.Empty;
    public int? Version { get; set; }
    public string DescriptorType { get; set; } = string.Empty;
    public Location Location { get; set; } = null!;
}
```

- [ ] **Step 2: Build generator + verify**

Run: `dotnet build framework/tools/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/RefValidationSourceGenerator.cs
git commit -m "feat: add RefValidationSourceGenerator — compile-time VersionedDescriptorRef validation"
```

---

### Task 1: DescriptorRefValidator — Runtime Validation Utility

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorRefValidator.cs`

A runtime utility that validates all refs in a descriptor against the global registry. Used in tests and at startup.

- [ ] **Step 1: Write DescriptorRefValidator.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorRefValidator
{
    public sealed class ValidationReport
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; init; } = new();
    }

    public static ValidationReport Validate(
        IDescriptor descriptor,
        IGlobalDescriptorRegistry registry)
    {
        var errors = new List<string>();

        switch (descriptor)
        {
            case CapabilityDescriptor c:
                ValidateRef(c.InputSchema, registry, errors, $"{c.Name}.InputSchema");
                ValidateRef(c.OutputSchema, registry, errors, $"{c.Name}.OutputSchema");
                break;

            case EventDescriptor e:
                ValidateRef(e.PayloadSchema, registry, errors, $"{e.Name}.PayloadSchema");
                break;

            case FormDescriptor f:
                ValidateRef(f.Schema, registry, errors, $"{f.Name}.Schema");
                break;

            case HumanTaskDescriptor h:
                ValidateRef(h.Form, registry, errors, $"{h.Name}.Form");
                if (h.InputSchema != null)
                    ValidateRef(h.InputSchema.Value, registry, errors, $"{h.Name}.InputSchema");
                if (h.OutputSchema != null)
                    ValidateRef(h.OutputSchema.Value, registry, errors, $"{h.Name}.OutputSchema");
                foreach (var outcome in h.Outcomes)
                {
                    if (outcome.Capability != null)
                        ValidateRef(outcome.Capability.Value, registry, errors, $"{h.Name}.Outcome.{outcome.Condition}");
                }
                break;

            case WorkflowDescriptor w:
                if (w.VariableSchema != null)
                    ValidateRef(w.VariableSchema.Value, registry, errors, $"{w.Name}.VariableSchema");
                foreach (var step in w.Steps)
                {
                    ValidateStepTarget(step, registry, errors);
                }
                break;
        }

        return new ValidationReport { Errors = errors };
    }

    private static void ValidateRef<T>(
        VersionedDescriptorRef<T> descriptorRef,
        IGlobalDescriptorRegistry registry,
        List<string> errors,
        string context) where T : IVersionedDescriptor
    {
        var resolved = registry.GetById(descriptorRef.Id);
        if (resolved == null)
        {
            errors.Add($"[{context}] Unresolved descriptor ref: {typeof(T).Name} '{descriptorRef.Id}' v{descriptorRef.Version}");
        }
        else if (resolved is IVersionedDescriptor versioned && versioned.Version < descriptorRef.Version)
        {
            errors.Add($"[{context}] Version conflict: {typeof(T).Name} '{descriptorRef.Id}' requires v{descriptorRef.Version} but latest is v{versioned.Version}");
        }
    }

    private static void ValidateStepTarget(
        WorkflowStep step,
        IGlobalDescriptorRegistry registry,
        List<string> errors)
    {
        switch (step.Target)
        {
            case CapabilityTarget ct:
                ValidateRef(ct.Capability, registry, errors, $"Step '{step.Id}' CapabilityTarget");
                break;
            case HumanTaskTarget ht:
                ValidateRef(ht.HumanTask, registry, errors, $"Step '{step.Id}' HumanTaskTarget");
                break;
            case SubWorkflowTarget sw:
                ValidateRef(sw.SubWorkflow, registry, errors, $"Step '{step.Id}' SubWorkflowTarget");
                break;
        }
    }
}
```

- [ ] **Step 2: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj
git add framework/src/CrestCreates.Metadata/DescriptorRefValidator.cs
git commit -m "feat: add DescriptorRefValidator — runtime validation of all VersionedDescriptorRefs"
```

---

### Task 2: Tests — DescriptorRefValidator

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorRefValidatorTests.cs`

- [ ] **Step 1: Write DescriptorRefValidatorTests.cs (5 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRefValidatorTests
{
    [Fact]
    public void Validate_ValidCapability_Passes()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor { Id = "schema_01", Name = "Test", Version = 1, State = DescriptorState.Active });

        var cap = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test.cap", Version = 1, State = DescriptorState.Active,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        var report = DescriptorRefValidator.Validate(cap, registry);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnresolvedRef_ReportsError()
    {
        var registry = new GlobalDescriptorRegistry();

        var cap = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test.cap", Version = 1, State = DescriptorState.Active,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_missing", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        var report = DescriptorRefValidator.Validate(cap, registry);
        report.IsValid.Should().BeFalse();
        report.Errors.Should().Contain(e => e.Contains("schema_missing"));
    }

    [Fact]
    public void Validate_Workflow_ChecksStepTargets()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor { Id = "wf_01", Name = "test", Version = 1, State = DescriptorState.Active });

        var wf = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_missing", 1)
                    }
                }
            }
        };

        var report = DescriptorRefValidator.Validate(wf, registry);
        report.Errors.Should().Contain(e => e.Contains("cap_missing"));
    }

    [Fact]
    public void Validate_ValidWorkflow_AllResolved()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new Capability.Abstractions.CapabilityDescriptor
        {
            Id = "cap_01", Name = "test", Version = 1, State = DescriptorState.Active
        });

        var wf = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<Capability.Abstractions.CapabilityDescriptor>("cap_01", 1)
                    }
                }
            }
        };

        var report = DescriptorRefValidator.Validate(wf, registry);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_HumanTask_ChecksOutcomeRefs()
    {
        var registry = new GlobalDescriptorRegistry();

        var ht = new HumanTask.Abstractions.HumanTaskDescriptor
        {
            Id = "ht_01", Name = "task", Version = 1, State = DescriptorState.Active,
            Form = new VersionedDescriptorRef<Form.Abstractions.FormDescriptor>("form_01", 1),
            Outcomes = new List<HumanTask.Abstractions.CompletionOutcome>
            {
                new()
                {
                    Condition = HumanTask.Abstractions.CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_missing", 1)
                }
            }
        };

        var report = DescriptorRefValidator.Validate(ht, registry);
        report.Errors.Should().Contain(e => e.Contains("cap_missing"));
    }
}
```

- [ ] **Step 2: Add HumanTask.Abstractions ref to Metadata.Tests.csproj** (already has it)

- [ ] **Step 3: Build + run tests + commit**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj
git add framework/test/CrestCreates.Metadata.Tests/DescriptorRefValidatorTests.cs
git commit -m "feat: add DescriptorRefValidatorTests — 5 tests for ref validation"
```

Expected: ~29 Metadata tests (24 existing + 5 new).

---

### Task 3: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Expected: ~181 tests pass.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 11 — Build-Time Ref Validation, 5 tests

- RefValidationSourceGenerator: compile-time validation of all VersionedDescriptorRef<T>
  constructions — reports CC1001 error for unresolved descriptor IDs
- DescriptorRefValidator: runtime utility validating all refs in any descriptor against
  the global registry — checks Capability, Event, Form, HumanTask, Workflow references
- Handles step targets (CapabilityTarget, HumanTaskTarget, SubWorkflowTarget)
- Handles HumanTask outcome capabilities
- 5 tests: valid capability, unresolved ref, workflow targets, valid workflow, outcomes
- ~181 total tests across all 11 phases"
```

---

## Phase 11 Summary

| Task | Component | Tests |
|------|-----------|-------|
| 0 | RefValidationSourceGenerator (compile-time) | — |
| 1 | DescriptorRefValidator (runtime) | 5 |
| 2 | Test files | — |
| 3 | Full build + commit | — |
| **Total** | **~2 new files** | **~5 new tests** |
