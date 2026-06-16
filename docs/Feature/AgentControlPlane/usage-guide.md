# LLM Bootstrap Plane — Usage Guide

> **Date:** 2026-06-16 | **Status:** Draft

## 1. Quick Start

### 1.1 Prerequisites

The LLM Bootstrap Plane depends on:
- Phase 7a: `CrestCreates.DescriptorDraft` (provides `IDescriptorDraftStore`, `IDescriptorDraftValidator`, etc.)
- Phase 6: `CrestCreates.Metadata` (provides Control Plane services)
- An `ILLMProvider` implementation (e.g., OpenAI, Anthropic, or local model)

### 1.2 DI Registration (Proposed)

```csharp
// Program.cs
builder.Services.AddDescriptorDrafts();          // Phase 7a
builder.Services.AddLLMBootstrapPlane(options => // Phase 7b
{
    options.DefaultProvider = "openai";
    options.PromptTemplatePath = "prompts/";
});
```

---

## 2. Generating a Descriptor Draft via LLM

### 2.1 Single Draft Generation

```csharp
public class SchemaDesigner
{
    private readonly ILLMProvider _llm;
    private readonly IDescriptorDraftStore _draftStore;
    private readonly IPromptTemplateRegistry _templates;

    public async Task<DescriptorDraft> GenerateSchemaDraftAsync(
        string intent, string tenantId)
    {
        // 1. Resolve prompt template for Schema
        var template = _templates.Get("schema-create-v1");

        // 2. Build prompt with context
        var prompt = template.UserPromptTemplate
            .Replace("{{intent}}", intent);

        // 3. Invoke LLM
        var response = await _llm.GenerateAsync(new LLMRequest
        {
            Prompt = prompt,
            TargetKind = DescriptorKind.Schema,
            SystemPrompt = template.SystemPrompt
        });

        if (!response.ParseErrors.Any())
        {
            // 4. Store the draft (triggers CreateClone for snapshot isolation)
            var draft = response.ParsedDraft!;
            await _draftStore.SaveAsync(draft);
            return draft;
        }

        throw new InvalidOperationException(
            $"LLM output parse failed: {string.Join("; ", response.ParseErrors)}");
    }
}
```

### 2.2 Batch Generation with Correlation

```csharp
public async Task<IReadOnlyList<DescriptorDraft>> GenerateBatchAsync(
    string intent, string tenantId)
{
    var correlationId = Guid.NewGuid().ToString("N");
    var templates = new[]
    {
        ("schema-create-v1", DescriptorKind.Schema),
        ("capability-create-v1", DescriptorKind.Capability),
        ("workflow-create-v1", DescriptorKind.Workflow)
    };

    var drafts = new List<DescriptorDraft>();
    foreach (var (templateId, kind) in templates)
    {
        var draft = await GenerateAsync(templateId, kind, intent, tenantId);
        drafts.Add(draft with { CorrelationId = correlationId });
    }

    // Save all correlated drafts
    foreach (var draft in drafts)
        await _draftStore.SaveAsync(draft);

    return drafts;
}
```

---

## 3. Prompt Templates

### 3.1 Template Structure

Prompt templates are externalized JSON/YAML files that define how LLMs should produce structured descriptor output:

```json
{
  "templateId": "schema-create-v1",
  "targetKind": "Schema",
  "version": 1,
  "systemPrompt": "You are a schema designer for the CrestCreates framework. Output valid SchemaDescriptor JSON. Do not include explanations.",
  "userPromptTemplate": "Create a schema for: {{intent}}. Existing schemas in inventory: {{inventory_summary}}",
  "outputSchema": {
    "fields": ["Id", "Name", "Fields[].Name", "Fields[].FieldType", "Fields[].IsRequired"],
    "required": ["Id", "Name"]
  },
  "contextHints": {
    "framework_version": "10.0",
    "schema_id_prefix": "schema_"
  }
}
```

### 3.2 Template Registry

```csharp
// Register templates at startup
var registry = new DefaultPromptTemplateRegistry();
registry.Register(PromptTemplate.FromFile("prompts/schema-create-v1.json"));
registry.Register(PromptTemplate.FromFile("prompts/capability-create-v1.json"));
registry.Register(PromptTemplate.FromFile("prompts/workflow-create-v1.json"));
```

### 3.3 Context Injection

Templates support placeholder injection for current inventory context:

| Placeholder | Description |
|-------------|-------------|
| `{{intent}}` | User's stated intention for the descriptor |
| `{{inventory_summary}}` | Summary of current descriptor inventory (IDs, kinds, versions) |
| `{{tenant_id}}` | Current tenant identifier |
| `{{correlation_id}}` | Batch correlation ID |
| `{{existing_relations}}` | Topology edges for related descriptors |

---

## 4. LLM Provider Configuration

### 4.1 OpenAI Provider (Example)

```csharp
builder.Services.AddLLMBootstrapPlane(options =>
{
    options.AddProvider("openai", provider =>
    {
        provider.Endpoint = "https://api.openai.com/v1/chat/completions";
        provider.Model = "gpt-4o";
        provider.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        provider.DefaultTemperature = 0.2f;   // Lower = more deterministic output
        provider.MaxTokens = 4096;
    });
});
```

### 4.2 Anthropic Provider (Example)

```csharp
builder.Services.AddLLMBootstrapPlane(options =>
{
    options.AddProvider("anthropic", provider =>
    {
        provider.Endpoint = "https://api.anthropic.com/v1/messages";
        provider.Model = "claude-sonnet-4-20250514";
        provider.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    });
});
```

### 4.3 Local Model Provider (Example)

```csharp
builder.Services.AddLLMBootstrapPlane(options =>
{
    options.AddProvider("local-llama", provider =>
    {
        provider.Endpoint = "http://localhost:11434/api/generate";
        provider.Model = "llama3.1:8b";
        provider.DefaultTemperature = 0.1f;
    });
});
```

---

## 5. Validation and Review

### 5.1 Validating LLM Output

Every LLM-generated draft MUST pass the same validator as human-authored drafts:

```csharp
var validator = services.GetRequiredService<IDescriptorDraftValidator>();
var validationResult = validator.Validate(llmGeneratedDraft);

if (!validationResult.IsValid)
{
    foreach (var diag in validationResult.Diagnostics)
        Console.WriteLine($"[{diag.Severity}] {diag.Code}: {diag.Message}");
    return; // Draft is invalid — do not save or proceed
}
```

### 5.2 Full Review Pipeline

```csharp
var reviewService = services.GetRequiredService<IDescriptorDraftReviewService>();
var currentInventory = await LoadCurrentInventoryAsync(tenantId);

var reviewResult = await reviewService.ReviewAsync(draft, currentInventory);

Console.WriteLine($"Review complete:");
Console.WriteLine($"  Validation: {(reviewResult.ValidationResult.IsValid ? "PASS" : "FAIL")}");
Console.WriteLine($"  Materialized: {reviewResult.MaterializationResult?.IsMaterialized ?? false}");
Console.WriteLine($"  Governance: {reviewResult.GovernanceDecision?.MaxDecision}");
Console.WriteLine($"  Activation Eligible: {reviewResult.IsActivationEligible}");
```

---

## 6. Error Handling

### 6.1 LLM Failures

| Scenario | Behavior |
|----------|----------|
| LLM timeout | Retry with exponential backoff (configurable) |
| LLM returns non-JSON | `LLMResponse.ParseErrors` populated; draft not saved |
| LLM returns valid JSON but invalid descriptor | Validator rejects; diagnostics recorded |
| LLM rate limit | Circuit breaker; fallback to alternative provider |

### 6.2 Parse Failures

The `DescriptorDraftBuilder` converts raw LLM output to typed `DescriptorDraftPayload` instances. Parse failures are recorded in `LLMResponse.ParseErrors` and do not create a draft.

```csharp
if (response.ParseErrors.Any())
{
    _logger.LogWarning("LLM parse errors for draft generation: {Errors}",
        string.Join("; ", response.ParseErrors));
    // The raw output is preserved for debugging but no draft is created
}
```

---

## 7. Best Practices

1. **Low temperature for schema generation** — Use temperature ≤ 0.2 for deterministic descriptor output.
2. **Prompt versioning** — Always version prompt templates; changes can alter generator behavior.
3. **Validate early** — Run validation immediately after generation; don't store invalid drafts.
4. **Correlate batches** — Use `CorrelationId` to group related drafts (e.g., schema + capability + workflow).
5. **Human review gate** — LLM-generated drafts with `IsActivationEligible = false` require human intervention.
6. **Inventory context is critical** — Always inject current inventory summary into prompts to avoid duplicate IDs.
7. **Dry-run mode** — Test prompts with `SaveAsync` disabled to iterate on prompt quality without polluting the store.

---

## 8. Examples

### 8.1 Generate a New Schema

```csharp
var draft = await designer.GenerateSchemaDraftAsync(
    "BlogPost with Title, Content, AuthorId, PublishedAt fields",
    tenantId: "tenant-1");

// draft.Payload is SchemaDescriptorDraftPayload
// draft.Payload.Descriptor.Fields contains the generated fields
```

### 8.2 Generate a Full Workflow

```csharp
var correlationId = Guid.NewGuid().ToString("N");

// Step 1: Generate the event and schemas
var eventDraft = await GenerateAsync("event-create-v1", DescriptorKind.Event, ...);
var inputSchemaDraft = await GenerateAsync("schema-create-v1", DescriptorKind.Schema, ...);
var outputSchemaDraft = await GenerateAsync("schema-create-v1", DescriptorKind.Schema, ...);

// Step 2: Generate the capability
var capDraft = await GenerateAsync("capability-create-v1", DescriptorKind.Capability,
    $"Uses input schema {inputSchemaDraft.DescriptorId}, produces event {eventDraft.DescriptorId}", ...);

// Step 3: Generate the workflow (references capability)
var wfDraft = await GenerateAsync("workflow-create-v1", DescriptorKind.Workflow,
    $"Workflow with step calling {capDraft.DescriptorId}", ...);

// Step 4: Review the entire batch
foreach (var draft in new[] { eventDraft, inputSchemaDraft, outputSchemaDraft, capDraft, wfDraft })
{
    var review = await reviewService.ReviewAsync(draft, currentInventory);
    Console.WriteLine($"{draft.DescriptorId}: {(review.IsActivationEligible ? "Ready" : "Needs Review")}");
}
```
