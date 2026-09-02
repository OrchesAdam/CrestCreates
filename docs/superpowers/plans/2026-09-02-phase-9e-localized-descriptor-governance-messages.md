# Phase 9e — Localized Descriptor Governance Messages Implementation Plan

> Date: 2026-09-02
> Issue: #27
> Design: `docs/superpowers/specs/2026-09-02-phase-9e-localized-descriptor-governance-messages-design.md`

## Goal

Close Issue #27 by making the existing 31 descriptor-governance `MessageTemplateId` projections culture/resource-backed while preserving all machine semantics and fallback behavior.

## Implementation rules

- Work test-first: add the named failing acceptance test before each production change.
- Do not change `MessageTemplateId`, `ReasonCode`, `ErrorCode`, DTO shapes, JSON contract versions, `TemplateVersion`, hashes, or decisions.
- Do not modify the general Localization platform unless a failing acceptance case proves the existing contract cannot support this bridge.
- Keep the English fallback messages byte-for-byte equal to current `master`.
- Do not use reflection scanning, runtime-generated serializers, regex compilation, or a second report-building path.

## Task 1 — Freeze the acceptance skeleton and English compatibility

**Modify**

- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorReviewMessageTemplateCatalogTests.cs`

**Steps**

1. Add the Phase 9e catalog/resource test names from the design.
2. Preserve existing assertions for all formatter edge cases and `TemplateVersion == "7d.v1"`.
3. Add a table-driven assertion covering the current English output for all 31 stable ids.
4. Run the catalog tests and confirm new culture/resource tests fail for the expected reason only.

**Command**

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests \
  --filter "FullyQualifiedName~DescriptorReviewMessageTemplateCatalogTests"
```

## Task 2 — Add explicit embedded resources and an AOT-safe resource catalog

**Create**

- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Localization/Resources/DescriptorReviewMessages.en.json`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Localization/Resources/DescriptorReviewMessages.zh-CN.json`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Localization/DescriptorReviewMessageResourceCatalog.cs`

**Modify**

- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CrestCreates.Agent.ControlPlane.csproj`
- catalog tests from Task 1

**Steps**

1. Copy the 31 current English templates exactly into the flat `en` resource.
2. Add `zh-CN` translations with identical placeholder names.
3. Embed both files with explicit logical names in the project file.
4. Implement a small internal catalog that opens only those known manifest names and parses them with `JsonDocument` into ordinal read-only dictionaries.
5. Implement exact culture lookup followed by string-based parent fallback (`en-US -> en`) without `CultureInfo` construction or assembly scanning.
6. Add the exact-set resource coverage test: both files must contain exactly the ids defined by `DescriptorReviewReportMessageTemplateIds` and `DescriptorActivationMessageTemplateIds`, with matching placeholder sets.
7. Run the focused tests.

## Task 3 — Connect `MessageTemplateId` to existing Localization

**Modify**

- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewMessageTemplateCatalog.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/CrestCreates.Agent.ControlPlane.csproj`
- catalog tests

**Steps**

1. Add the implementation-project reference to `CrestCreates.Localization`; do not touch Control Plane Abstractions.
2. Give the catalog an optional `ILocalizationService` and logger while retaining parameterless construction compatibility.
3. Select the template in the frozen order: existing Localization service -> built-in exact/parent resource -> unchanged English fallback -> unknown-id sentinel.
4. Treat null, empty, whitespace, and a value equal to the key as a localization miss.
5. Catch provider/resource lookup failures, log a warning, and continue to the stable fallback.
6. Apply the existing hand-written named-placeholder substitution only after template selection.
7. Make the new culture, external-contributor, missing-resource, provider-failure, and no-service tests pass.

## Task 4 — Wire the one DI mainline

**Modify**

- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneServiceCollectionExtensions.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/AgentControlPlaneTestBase.cs` or a focused Phase 9e composition test file

**Steps**

1. Replace the three duplicated direct catalog registrations with one private helper/factory.
2. Resolve `ILocalizationService` optionally and the logger normally.
3. Prove default, options, and legacy-policy `AddAgentControlPlane(...)` overloads resolve the same localized catalog behavior.
4. Prove `AddAgentControlPlane(...)` still resolves the catalog when Localization is absent.
5. Do not introduce service location inside report builders, renderers, handlers, or DTOs.

## Task 5 — Prove semantic preservation through report composition

**Modify**

- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/DescriptorReviewReportBuilderTests.cs`
  or create `Phase9eDescriptorGovernanceLocalizationTests.cs`

**Steps**

1. Reuse an existing deterministic review fixture; do not create a second builder fixture model.
2. Build the same review under `en` and `zh-CN` catalogs.
3. Assert at least one `Message` differs.
4. Assert `ReasonCode`, `MessageTemplateId`, `Parameters`, severity, related ids, `ReviewResultId`, `SourceReviewHash`, `TemplateVersion`, governance decision, activation eligibility, and any canonical/binding hash inputs are equal.
5. Confirm renderers still consume the already-projected DTO `Message` and gain no Localization dependency.

## Task 6 — Lock regression evidence outside descriptor governance

**Inspect/run; modify only if evidence is absent**

- `tests/Framework/Web/CrestCreates.Web.Tests/Middlewares/DefaultCrestExceptionConverterTests.cs`
- validation localization tests
- `tests/Framework/Infrastructure/CrestCreates.Localization.Tests/LocalizationServiceTests.cs`
- `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`

**Steps**

1. Confirm permission 401/403 localization preserves `Crest.Auth.Unauthorized` / `Crest.Auth.Forbidden`.
2. Confirm validation localization preserves its stable error code.
3. Confirm contributor resolution order is deterministic under the existing priority contract; add only a focused regression test if the fact is not already executable evidence.
4. Confirm `ControlPlane.Abstractions` still has no Framework/Localization reference and Runtime still has no Web/Platform dependency.
5. Do not refactor LocalizationService as part of #27 unless one of these frozen cases fails due to a real contract defect.

## Task 7 — Extend the Control Plane NativeAOT gate

**Modify**

- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.csproj`
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture/Program.cs`
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests/ControlPlaneJsonContractsAotFixtureTests.cs`

**Steps**

1. Reference the Control Plane implementation from the existing first-party Control Plane fixture.
2. Instantiate/resolve the real catalog with a deterministic fake `ILocalizationService` whose current culture is `zh-CN` and whose lookup returns the key.
3. Format a template containing a parameter and assert the built-in Chinese resource was loaded and substituted.
4. Print `CONTROL_PLANE_LOCALIZED_MESSAGE_NATIVEAOT_OK` only on success.
5. Publish `linux-x64` with `CrestCreatesPublishMode=aot`, execute the native binary, and assert the marker plus the existing JSON-contract markers.
6. Keep IL2026/IL3050 warnings fail-closed.

## Task 8 — Verification and closure record

**Modify after all gates pass**

- `memory.md`
- Issue #27 comment/body as directed by the maintainer

**Commands**

```bash
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
dotnet test tests/Framework/Infrastructure/CrestCreates.Localization.Tests
dotnet test tests/Framework/Web/CrestCreates.Web.Tests \
  --filter "FullyQualifiedName~DefaultCrestExceptionConverterTests"
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.JsonContracts.AotFixture.Tests
dotnet build solutions/CrestCreates.All.slnx --no-restore
```

If the full solution build requires a restore in a clean environment, run the normal build once and then repeat `--no-restore` for the recorded verification.

**Closure evidence**

- record test totals and NativeAOT marker;
- state explicitly that only presentation `Message` is culture-dependent;
- state that stable ids, DTOs, `7d.v1`, hashes, decisions, and existing exception/validation/permission code semantics did not change;
- close #27 only after the resource exact-set test and native publish-link-run fixture pass.
