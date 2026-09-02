# Phase 9e — Localized Descriptor Governance Messages Design

> Date: 2026-09-02
> Status: Approved for implementation
> Issue: #27
> Depends on: Phase 7d review-report contracts, semantic string governance, existing Localization module

## 1. Current-master alignment

Issue #27 originally described a broad locale-support phase. Current `master` has already closed most of that foundation:

- `ILocalizationService` / `LocalizationService` provide culture scopes, resource contributors, parent-culture fallback, and `IStringLocalizer` fallback.
- framework exceptions resolve presentation text from stable `ErrorCode` values;
- validation and permission failures retain stable machine-readable codes;
- `MessageTemplateId` is already a typed semantic value;
- descriptor review reports already carry `ReasonCode + MessageTemplateId + Parameters + Message`;
- `DefaultDescriptorReviewMessageTemplateCatalog` deterministically formats 31 stable template ids and is NativeAOT-oriented.

The remaining gap is narrow: `DefaultDescriptorReviewMessageTemplateCatalog` still selects its final presentation template exclusively from a hard-coded English dictionary. Descriptor governance messages therefore do not vary by culture or resource.

## 2. Goal

Connect the existing stable `MessageTemplateId` model to culture/resource-backed presentation without changing descriptor-governance semantics.

```text
ReasonCode / ErrorCode / MessageTemplateId / Parameters
    = stable machine semantics

culture + localized resource
    = presentation selection

selected template + deterministic named-placeholder substitution
    = Message
```

## 3. Boundary

### 3.1 In scope

1. Make all 31 existing descriptor review and activation `MessageTemplateId` values resource-backed.
2. Provide first-party `en` and `zh-CN` resources in the Agent Control Plane implementation assembly.
3. Use the existing `ILocalizationService` as the culture and contributor bridge when it is registered.
4. Preserve an embedded, stable English fallback when a localized resource is absent or localization resolution fails.
5. Preserve the existing deterministic named-placeholder formatter.
6. Wire the catalog through existing `AddAgentControlPlane(...)` registrations without making Localization mandatory for hosts that currently omit it.
7. Prove the embedded-resource path in a real NativeAOT publish-link-run fixture.

### 3.2 Out of scope

- no new Localization platform, database-backed translation store, hot reload, admin UI, or language switcher;
- no business-domain translation content;
- no ICU message format, pluralization, gender rules, or locale-aware numeric/date formatting;
- no new `ErrorCode`, `ReasonCode`, `MessageTemplateId`, diagnostic, or permission semantics;
- no DTO shape or JSON contract-version change;
- no localization of section titles or other literals that do not already flow through the template catalog;
- no change to governance, activation, authorization, validation, hash, persistence, delivery, or workflow decisions;
- no reflection scanning of assemblies or resources;
- no second report builder or renderer path.

## 4. Invariants

### I1 — Machine semantics are culture-invariant

Changing culture may change only `Message`. It must not change:

- `ReasonCode`;
- `ErrorCode`;
- `MessageTemplateId`;
- `Parameters` keys or values;
- severity;
- related diagnostic/descriptor ids;
- governance or activation decision;
- `ReviewResultId`, `SourceReviewHash`, canonical hashes, or binding hashes.

### I2 — Existing public contracts remain stable

`IDescriptorReviewMessageTemplateCatalog` remains:

```csharp
string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters);
string TemplateVersion { get; }
```

`TemplateVersion` remains `7d.v1`. Phase 9e adds locale projections but does not change the id set, parameter schema, formatter grammar, or report contract.

### I3 — Localization is optional at composition time

`AddAgentControlPlane(...)` must still resolve and execute when `ILocalizationService` is not registered. The result then uses the stable English fallback.

### I4 — Fallback is deterministic and fail-safe

Template selection order is fixed:

1. existing `ILocalizationService` lookup for the catalog's current culture;
2. built-in resource for the exact culture;
3. built-in parent culture, repeatedly removing the final `-segment`;
4. stable English fallback template;
5. existing `[Unknown template: {id}]` sentinel when the id is unknown.

Whitespace, a value equal to the lookup key, a missing resource, or a localization-provider exception counts as a miss and proceeds to the next step.

### I5 — Formatting behavior is unchanged

- named `{Key}` placeholders use ordinal parameter lookup;
- a missing parameter leaves its placeholder intact;
- extra parameters are ignored;
- substitution remains hand-written and does not introduce regex compilation, runtime IL emit, or reflection.

### I6 — Resource coverage is exact

The first-party `en` and `zh-CN` resource key sets equal the 31 existing ids. No anonymous resource-only template id is allowed, and no existing id may be omitted.

### I7 — Resource loading is NativeAOT-safe

Resource names are explicit build-time logical names. Loading uses known manifest-resource names and AOT-safe JSON parsing. Production code must not enumerate assemblies, scan types, use `ResourceManager` by reflected type discovery, or enable reflection-based JSON fallback.

## 5. Design

### 5.1 Components

```text
DefaultDescriptorReviewReportBuilder
        |
        v
IDescriptorReviewMessageTemplateCatalog.Format(id, parameters)
        |
        v
DefaultDescriptorReviewMessageTemplateCatalog
        |-- optional ILocalizationService lookup
        |-- built-in exact/parent culture resource lookup
        |-- stable English fallback
        v
deterministic named-placeholder substitution
        |
        v
DescriptorReviewReportItemDto.Message / recommendation Message
```

### 5.2 Resource convention

Resources live with the owning projection implementation:

```text
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/
  Localization/Resources/
    DescriptorReviewMessages.en.json
    DescriptorReviewMessages.zh-CN.json
```

They are embedded with explicit logical names. Each file is a flat JSON object:

```json
{
  "report.summary.valid": "...{DiagnosticCount}..."
}
```

Nested JSON is not introduced. Placeholder names must match the existing English fallback exactly.

### 5.3 Internal resource catalog

Add an internal resource catalog owned by Agent Control Plane. It:

- loads only the two explicit manifest names;
- parses each resource once and caches immutable ordinal dictionaries;
- performs exact/parent culture lookup using culture-name strings;
- exposes no public framework contract;
- treats load/parse failure as a miss so the stable fallback remains available;
- reports failures through the catalog logger without including report parameters as structured secrets.

### 5.4 Localization-service bridge

`DefaultDescriptorReviewMessageTemplateCatalog` accepts an optional `ILocalizationService` and logger. `Format` asks the existing service for the stable `MessageTemplateId` key using the service's current culture. This preserves module-contributed overrides and culture scopes.

The three `AddAgentControlPlane(...)` overloads use one private registration helper/factory so behavior cannot drift. A parameterless construction path remains for existing tests and direct consumers.

The Agent Control Plane implementation project may reference `CrestCreates.Localization`; `CrestCreates.Agent.ControlPlane.Abstractions` must not.

## 6. Case matrix

| Family | Case | Expected result |
|---|---|---|
| Happy | `zh-CN` + known id + complete parameters | Chinese message with substituted values |
| Happy | `en` + known id | Original English output byte-for-byte |
| Happy | external localization contributor returns a value | Contributor value wins, then uses the same named formatter |
| Boundary | `zh-CN` localized resource omits a key | Stable English template for that id |
| Boundary | `en-US` with no exact resource | Parent `en` resource |
| Boundary | no `ILocalizationService` registered | Stable English output; Control Plane DI still resolves |
| Boundary | missing parameter | Placeholder remains unchanged |
| Boundary | extra parameter | Extra value is ignored |
| Boundary | unknown template id | Existing unknown-template sentinel |
| Failure | provider returns null/empty/whitespace/key | Continue deterministic fallback chain |
| Failure | provider throws | Log and use built-in/stable fallback; report build does not fail |
| Failure | embedded localized resource unavailable | Stable English fallback |
| Composition | same review built under `en` and `zh-CN` | Only presentation messages differ |
| Composition | permission/validation localization | Stable ErrorCode remains unchanged |
| Composition | NativeAOT published fixture | Embedded `zh-CN` resource loads and formats successfully |

## 7. Acceptance test skeleton

### Catalog/resource tests

- `DescriptorGovernanceMessage_Should_Resolve_ByCurrentCulture`
- `DescriptorGovernanceMessage_Should_Resolve_ExternalContributor_BeforeBuiltInResource`
- `DescriptorGovernanceParentCulture_Should_FallbackToEn`
- `DescriptorGovernanceLocalizationMissing_Should_FallbackToStableTemplate`
- `DescriptorGovernanceLocalizationFailure_Should_FallbackToStableTemplate`
- `DescriptorGovernanceWithoutLocalizationService_Should_PreserveEnglishBehavior`
- `DescriptorGovernanceResources_Should_CoverExactStableTemplateIdSet`
- retain all existing formatter tests for missing/extra parameters, unknown ids, determinism, and `7d.v1`

### Semantic-preservation tests

- `DescriptorGovernanceLocalization_Should_Preserve_ReasonCode`
- `DescriptorGovernanceLocalization_Should_Preserve_MessageTemplateId`
- `DescriptorGovernanceLocalization_Should_Preserve_Parameters`
- `DescriptorGovernanceLocalization_Should_Not_Change_CanonicalHash_OrDecision`
- `PermissionLocalization_Should_Preserve_StableErrorCode` (existing evidence may satisfy this)
- `ValidationLocalization_Should_Preserve_StableErrorCode` (existing evidence may satisfy this)
- `MultipleLocalizationContributors_Should_Not_Change_DeterministicResolutionOrder` (existing foundation evidence may satisfy this)

### Composition/AOT tests

- all three `AddAgentControlPlane(...)` overloads resolve the localized catalog consistently;
- Control Plane resolves without Localization registration;
- the existing Control Plane NativeAOT fixture prints and asserts `CONTROL_PLANE_LOCALIZED_MESSAGE_NATIVEAOT_OK` after loading and formatting a built-in `zh-CN` template.

## 8. Exit criteria

Issue #27 is complete when:

1. every existing descriptor-governance `MessageTemplateId` has `en` and `zh-CN` resource coverage;
2. current culture changes final descriptor-governance `Message` through the existing catalog mainline;
3. missing or failed localization deterministically returns the original English message;
4. machine semantics, hashes, decisions, DTO shape, and `TemplateVersion` remain unchanged;
5. targeted unit/composition tests, dependency boundaries, full build, and the Control Plane NativeAOT publish-link-run gate pass;
6. no alternative builder, renderer, localization platform, or runtime reflection path is introduced.
