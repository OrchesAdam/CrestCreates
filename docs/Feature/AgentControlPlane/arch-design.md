# LLM Bootstrap Plane — Architecture Design

> **Date:** 2026-06-16 | **Status:** Draft | **Phase 7b Foundation**

## 1. Overview

### 1.1 Goal

The LLM Bootstrap Plane bridges the deterministic Descriptor Draft Runtime (Phase 7a) with non-deterministic LLM-powered descriptor generation. It provides the initial authoring surface where LLMs, agents, and human-designer tools produce structured descriptor drafts that can be validated, materialized, and reviewed through the existing Control Plane pipeline.

### 1.2 Position in the Framework

```
LLM / Agent / Human Designer
        ↓
   LLM Bootstrap Plane       ← THIS DOCUMENT
        ↓
   DescriptorDraft           ← Phase 7a (implemented)
        ↓
   ReviewService             ← Phase 7a
        ↓
   Control Plane             ← Phase 6
        ↓
   ReviewResult
        ↓
   [STOP] (Phase 7a) / [Activate] (Phase 7e)
```

### 1.3 Design Principles

1. **Input validation before ingestion** — LLM output is non-deterministic. Every proposed descriptor must pass the same `IDescriptorDraftValidator` as human-authored drafts.
2. **The draft is the contract** — the LLM produces a structured `DescriptorDraft`, not raw text. The Draft Runtime is the single ingestion point.
3. **Prompt as configuration, not code** — prompt templates are externalized; the plane orchestrates prompt → LLM → draft pipeline.
4. **Deterministic review, non-deterministic generation** — LLM produces candidates; the Control Plane determines governance outcome.
5. **AoT-safe dispatch** — LLM integration does not introduce runtime reflection or dynamic code paths.

---

## 2. Architecture

### 2.1 Component Diagram

```
┌─────────────────────────────────────────────────────┐
│                 LLM Bootstrap Plane                  │
│                                                      │
│  ┌──────────────┐   ┌──────────────┐                │
│  │ Prompt        │   │ LLM           │               │
│  │ Template      │ → │ Provider      │               │
│  │ Registry      │   │ (pluggable)   │               │
│  └──────────────┘   └──────┬───────┘               │
│                             │                        │
│                             ↓                        │
│                    ┌────────────────┐                │
│                    │ Draft Builder   │               │
│                    │ (structured     │               │
│                    │  output →       │               │
│                    │  DescriptorDraft)│              │
│                    └───────┬────────┘               │
│                             │                        │
│  ┌──────────────────────────┼──────────────────────┐ │
│  │         DescriptorDraft (Phase 7a)              │ │
│  │  Store → Validator → Materializer → Review       │ │
│  └──────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### 2.2 Key Components

| Component | Responsibility | Status |
|-----------|---------------|--------|
| `PromptTemplate` | Structured prompt with descriptor context injection | Proposed |
| `ILLMProvider` | Abstract LLM invocation (pluggable backend) | Proposed |
| `PromptTemplateRegistry` | Stores and resolves prompt templates by descriptor kind | Proposed |
| `DescriptorDraftBuilder` | Converts LLM structured output to `DescriptorDraft` instances | Proposed |
| `DescriptorDraft` (Phase 7a) | Receives and stores the generated draft | **Implemented** |
| `IDescriptorDraftValidator` (Phase 7a) | Validates LLM-generated drafts against same rules | **Implemented** |

### 2.3 LLM Provider Abstraction

```csharp
public interface ILLMProvider
{
    Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default);
}

public sealed record LLMRequest
{
    public required string Prompt { get; init; }
    public required DescriptorKind TargetKind { get; init; }
    public IReadOnlyList<IDescriptor>? CurrentInventory { get; init; }
    public string? SystemPrompt { get; init; }
    public int? MaxTokens { get; init; }
    public float? Temperature { get; init; }
}

public sealed record LLMResponse
{
    public required string RawOutput { get; init; }
    public required DescriptorDraft? ParsedDraft { get; init; }
    public required IReadOnlyList<string> ParseErrors { get; init; }
    public required string? ModelId { get; init; }
    public required int TokensUsed { get; init; }
}
```

### 2.4 Prompt Template Design

Prompts are structured to produce deterministic descriptor JSON/schema output:

```csharp
public sealed record PromptTemplate
{
    public required string TemplateId { get; init; }
    public required DescriptorKind TargetKind { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPromptTemplate { get; init; }  // with {{placeholders}}
    public IReadOnlyDictionary<string, string>? ContextHints { get; init; }
}
```

---

## 3. Integration with DescriptorDraft (Phase 7a)

### 3.1 Author Kind

LLM-generated drafts use `DescriptorDraftAuthorKind.Agent` (or a new `LLM` variant). This distinguishes them from `Human`- and `System`-authored drafts in the store.

### 3.2 Validation Pipeline

```
LLM Output → DescriptorDraftBuilder → DescriptorDraft
    → IDescriptorDraftValidator (same as human drafts)
    → Materializer (same)
    → ReviewService (same)
```

No special validation path. LLM drafts must pass the same checks as human-authored ones — descriptor identity, version consistency, kind/payload match.

### 3.3 Multi-Draft Generation

An LLM prompt may produce multiple drafts (e.g., a workflow + its capability steps + schemas). The Bootstrap Plane supports batch generation with correlation via `DescriptorDraft.CorrelationId`.

---

## 4. Boundary Rules

| Rule | Rationale |
|------|-----------|
| LLM output is always wrapped in a `DescriptorDraft` | The Draft Runtime is the single ingestion point |
| LLM does not directly mutate registries | All changes go through draft → validate → materialize → review |
| Prompt templates are versioned | Prompt changes can change LLM output behavior |
| LLM provider is pluggable | Different backends (OpenAI, Anthropic, local) via `ILLMProvider` |
| No LLM calls in the Control Plane | LLM is authoring-only; review is deterministic |
| No runtime reflection | All dispatch is switch-based by `DescriptorKind` |

---

## 5. Future Phases

| Phase | Capability |
|-------|-----------|
| 7b | LLM draft generation with prompt templates |
| 7c | Multi-draft batch generation with correlation |
| 7d | Agent tool surface (MCP projection) |
| 7e | Activation workflow (reviewed drafts → runtime) |
| 7f | Continuous improvement loop (runtime feedback → prompt refinement) |

---

## 6. Project Layout (Proposed)

```
framework/src/CrestCreates.LLMBootstrap.Abstractions/
  ILLMProvider.cs
  LLMRequest.cs
  LLMResponse.cs
  PromptTemplate.cs
  IPromptTemplateRegistry.cs

framework/src/CrestCreates.LLMBootstrap/
  DefaultPromptTemplateRegistry.cs
  DescriptorDraftBuilder.cs
  LLMBootstrapServiceCollectionExtensions.cs

framework/test/CrestCreates.LLMBootstrap.Tests/
  DescriptorDraftBuilderTests.cs
  PromptTemplateRegistryTests.cs
```
