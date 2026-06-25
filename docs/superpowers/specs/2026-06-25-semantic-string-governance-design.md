# Semantic String Governance Design

Date: 2026-06-25

## Context

CrestCreates currently has many stable semantic strings spread across runtime,
metadata, framework, tooling, generated code, and tests. Examples include
business error codes, activation diagnostics, event names, permission names,
policy names, descriptor ids, human task ids, and Roslyn diagnostic ids.

Some areas already use local constant classes, such as feature management,
agent draft contracts, metadata context packs, and descriptor package
diagnostics. Other areas still embed values directly in implementation and
tests. This makes maintenance fragile, weakens symbol-based navigation, and
encourages new code to keep adding string literals as platform contracts.

The goal is to make stable platform semantics centralized, symbolized, and
typed without treating every technical string in the repository as a domain
identity.

## Goals

- Centralize stable semantic string values.
- Make callers use symbols instead of repeating string literals.
- Provide semantic value objects for runtime code.
- Preserve `const string` values for attributes, source generators, generated
  code, and Roslyn APIs that require compile-time constants.
- Keep files named after their main type.
- Organize constants by owning feature and semantic category.
- Add a lightweight guard so new code does not reintroduce high-value semantic
  literals.

## Non-Goals

- Do not eliminate every string literal in the codebase.
- Do not introduce runtime registries, scanners, reflection-based validation,
  or service-location for semantic names.
- Do not force wire DTOs, EF entities, JSON contracts, or persistence models to
  expose value objects in this phase.
- Do not turn HTTP methods, route fragments, configuration section names,
  database names, message headers, or log templates into semantic value objects
  unless they are also official platform identities.
- Do not maintain a long-term duplicate path where both scattered literals and
  centralized symbols are considered valid for the same semantic value.

## Architectural Direction

Use a two-shape model:

1. `const string` for compile-time and wire-bound usage.
2. Small readonly value objects for runtime semantic usage.

The bottom-level semantic value objects live in
`src/Core/CrestCreates.Core.Abstractions/Identity/`, because they must be usable
by Framework, Metadata, Runtime, Tooling-facing contracts, and sample code
without introducing upward references.

Feature-specific constants stay in their owning abstraction or shared project.
This avoids a global constants dumping ground and preserves ownership. For
example, activation governance strings belong in Agent Control Plane
abstractions, schema validation strings belong in Schema abstractions, and
canonical hash diagnostic ids belong with the code generator diagnostic surface.

## Core Value Objects

Add one file per type under `CrestCreates.Core.Abstractions/Identity/`:

- `ErrorCode`
- `DiagnosticCode`
- `EventName`
- `PermissionName`
- `PolicyName`
- `CapabilityId`
- `WorkflowId`
- `HumanTaskId`
- `DescriptorId`
- `VersionKey`

Each type is a `readonly record struct` with:

- `string Value`
- constructor validation rejecting null, empty, or whitespace values
- `ToString()` returning `Value`
- implicit conversion to `string`

The types intentionally do not validate naming conventions. Naming rules differ
between diagnostics (`CCHASH001`, `ACTIVATION_*`), permission names
(`agent.draft.create`), and descriptor ids (`schema.T1`). Convention enforcement
belongs to constants, tests, or future analyzers, not to the shared value
objects.

## Constant Class Pattern

Owning features expose both forms:

```csharp
public static class DescriptorActivationErrorCodes
{
    public const string InvalidStatusForRejectionValue =
        "ACTIVATION_INVALID_STATUS_FOR_REJECTION";

    public static ErrorCode InvalidStatusForRejection =>
        new(InvalidStatusForRejectionValue);
}
```

Use this pattern for all migrated semantic categories:

- `*ErrorCodes`
- `*DiagnosticCodes`
- `*EventNames`
- `*PermissionNames`
- `*PolicyNames`
- `*CapabilityIds`
- `*WorkflowIds`
- `*HumanTaskIds`
- `*DescriptorIds`
- `*VersionKeys`
- `*MessageTemplateIds` when the value is a stable review/report template key

Rules:

- File name matches the class name.
- Use plural semantic suffixes for new or renamed classes.
- The compile-time constant is named `XxxValue`.
- The runtime value object property is named `Xxx`.
- Existing single-shape classes should be migrated, not mirrored by a second
  permanent class.
- Attribute, source generator, generated-code, and Roslyn `DiagnosticDescriptor`
  sites use `XxxValue`.
- Runtime business logic, exception creation, permission checks, policy
  construction, and DTO assembly use the typed property unless the target API
  still requires string.

## Scope of Migration

This governance pass covers stable semantic identities across the repository.

Must migrate:

- Business and platform error codes, including feature management, validation,
  activation, agent draft contracts, and web/concurrency/auth exception codes.
- Runtime diagnostics and reason codes, including activation diagnostics,
  descriptor binding issues, descriptor package diagnostics, context pack
  diagnostics, report item reason codes, and fix proposal reason codes.
- Permission and policy names, including agent tool permissions,
  feature-management permissions, generated entity CRUD permission values, and
  permission policy prefixes when they represent official policy names.
- Stable event names and message template ids used as platform contracts.
- Stable descriptor, capability, workflow, human task, and version identifiers.
- Tooling/Roslyn diagnostic ids such as canonical hash and object mapping
  generator diagnostics.
- Tests that assert these official values.

Do not force-migrate:

- HTTP methods.
- Route segments that are not semantic identities.
- JSON property names and serializer contract details.
- Configuration section names.
- EF table, column, index, and migration literals.
- Kafka/RabbitMQ headers and transport protocol keys.
- Log message templates.
- Test data values that are just scenario inputs.

If a value fits both categories, semantic identity wins. For example, a
generated permission value must be centralized even if it is serialized later.

## Compatibility Strategy

Avoid a breaking repository-wide type flip in public DTOs and persistence
models. Most wire and storage contracts should remain string-backed in this
phase.

Add typed overloads or helper APIs at key runtime entry points:

- `CrestException` / `CrestBusinessException` accept `ErrorCode`.
- Permission/policy helpers accept `PermissionName` where practical.
- Runtime request builders may accept typed ids while storing string values in
  wire DTOs.

The current string properties can remain for compatibility, but new call sites
should pass symbols from owning constant classes. This keeps the external wire
contract stable while improving internal call sites.

## Source Generator and Tooling Rules

Generated code must follow the same pattern where it emits stable semantic
values. For example generated permission classes should emit:

```csharp
public const string CreateValue = "Book.Create";
public static PermissionName Create => new(CreateValue);
```

Generator internals should avoid direct diagnostic id literals:

- Put Roslyn diagnostic ids in centralized constants.
- Use those constants when creating `DiagnosticDescriptor`.
- Tests should reference the centralized ids where possible.

Generators can still output string literals into generated source when the
literal is the declared `*Value` constant. The important rule is that generated
callers and maintainers see one authoritative symbol for the stable value.

## Test and Guard Strategy

Add focused tests rather than a broad "no string literals" rule.

Required tests:

- Core value object tests for validation, equality, `ToString()`, and implicit
  string conversion.
- Exception overload tests proving typed error codes preserve the same wire
  error code.
- Representative migration tests for activation diagnostics, schema validation
  error codes, permission names, and canonical hash diagnostic ids.
- Generator tests proving generated permission constants expose both `XxxValue`
  and typed `Xxx` members.

Add a lightweight architecture test that scans C# source text for reintroduced
high-value semantic literals outside allowed definition files.

The guard should catch patterns such as:

- `Code = "ACTIVATION_..."`
- `ErrorCode = "FIELD_REQUIRED"`
- `new DiagnosticDescriptor(id: "CCHASH...")`
- `PermissionName = "agent...."`
- `HumanTaskId = "descriptor-activation-review"`

Allowed locations include:

- `*ErrorCodes.cs`
- `*DiagnosticCodes.cs`
- `*EventNames.cs`
- `*PermissionNames.cs`
- `*PolicyNames.cs`
- `*CapabilityIds.cs`
- `*WorkflowIds.cs`
- `*HumanTaskIds.cs`
- `*DescriptorIds.cs`
- `*VersionKeys.cs`
- generated-source assertion strings where no symbol exists yet during the
  generator test setup
- migrations and fixture-only test input files

The guard is intentionally pattern-based. It should prevent regressions in
known semantic families without becoming a noisy general-purpose analyzer.

## Implementation Order

1. Add core semantic value objects and tests.
2. Add typed exception overloads and tests.
3. Migrate foundational shared/framework error codes and permissions.
4. Migrate Metadata and Schema diagnostic/error constants.
5. Migrate Runtime Agent/Activation, Capability, Workflow, HumanTask, Eventing,
   and Audit semantic strings.
6. Migrate Tooling diagnostic ids and generated permission output.
7. Update tests to reference centralized constants for official semantic
   values.
8. Add the architecture guard after migrations so it enforces the new mainline.
9. Run targeted project tests, boundary tests, and a full build/test pass as
   feasible.

## Acceptance Criteria

- Stable semantic strings are owned by feature-specific constant classes.
- Runtime code uses value objects at construction/call sites where the target
  API supports them.
- Attribute, source generator, generated-code, and Roslyn sites use
  compile-time `XxxValue` constants.
- No long-term duplicate constant classes remain for the same semantic family.
- Existing wire contracts remain string-compatible.
- Boundary tests still pass.
- Generated permission output follows the `XxxValue` + typed `Xxx` pattern.
- Architecture guard fails on representative new inline semantic literals.
- Full repository build and targeted tests pass, or any environmental blocker is
  documented explicitly.
