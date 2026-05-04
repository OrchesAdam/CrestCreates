# Global Exception Handling Design

## Goal

Upgrade exception handling into a framework-level platform capability.

The main outcome is a single error response contract and a single formal runtime path for API errors across MVC controllers, generated Dynamic API endpoints, and generated CRUD endpoints.

The response contract is:

```json
{
  "code": "Crest.Concurrency.Conflict",
  "message": "Data was modified by another user. Refresh and retry.",
  "details": "Book id=... conflict",
  "traceId": "0HN...",
  "statusCode": 409
}
```

`code` is a stable platform or business error code. `statusCode` is the HTTP status code.

## Scope

| Area | Decision |
|---|---|
| Formal middleware | Keep `CrestCreates.AspNetCore.Middlewares.ExceptionHandlingMiddleware` as the only maintained exception middleware. |
| Legacy Web copy | Delete `framework/src/CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs`; it is already excluded from compilation and should not remain as a second path. |
| Error response | Replace numeric `Code` semantics with stable string `Code`; add explicit `StatusCode`. |
| Exception model | Add a platform exception base and align existing concurrency/precondition exceptions to it. |
| Localization | Resolve public messages through `CrestCreates.Localization.Services.ILocalizationService` by `ErrorCode`. |
| Main chains | MVC, generated Dynamic API, and generated CRUD all surface errors through the same middleware. |

## Non-Goals

| Non-goal | Reason |
|---|---|
| Do not introduce `ProblemDetails` as the public contract | It would create a second response shape and does not match existing `Code/Message` envelope style. |
| Do not keep enhancing the excluded Web middleware copy | It is not the formal runtime path. |
| Do not add runtime reflection scanning | This feature is runtime middleware behavior; generated API paths should continue to throw normally into the middleware. |
| Do not convert every existing `InvalidOperationException` in the repository in one pass | The first implementation should create the platform path and update the high-risk main chain. Wider cleanup can follow incrementally. |

## Current State

| File / symbol | Current behavior | Problem |
|---|---|---|
| `CrestCreates.AspNetCore.Middlewares.ExceptionHandlingMiddleware` | Catches exceptions and writes `{ Code, Message, Details, TraceId }`. | `Code` is currently an HTTP number, messages are hard-coded, and mapping logic is embedded in the middleware. |
| `CrestCreates.Web.Middlewares.ExceptionHandlingMiddleware` | Similar old middleware copy. | It is excluded by `Compile Remove`, but still misleads search and review. |
| `CrestAppServiceBase` | Wraps many failures into generic `Exception`; concurrency and precondition exceptions are already rethrown. | Platform/known exceptions can still be accidentally turned into generic 500 errors unless the whitelist is widened. |
| `GeneratedDynamicApiRuntime` | Throws permission, validation, transaction, and service exceptions outward. | Good direction; it should keep throwing into the global middleware. |
| Existing exceptions | `CrestPermissionException`, `CrestConcurrencyException`, `CrestPreconditionRequiredException`. | They are not unified under one base exception model. |
| Localization | `CrestCreates.Localization` has `ILocalizationService`. | Exception middleware does not use it yet. |

## Architecture

| Component | Location | Responsibility |
|---|---|---|
| `CrestException` | `CrestCreates.Domain.Shared.Exceptions` | Base class for platform exceptions. Carries `ErrorCode`, `HttpStatusCode`, safe `Details`, and safe fallback message. |
| Common platform exceptions | `CrestCreates.Domain.Shared.Exceptions` or existing domain exception namespace with compatibility forwarding | Business, validation, entity not found, concurrency, and precondition exceptions. |
| `CrestErrorResponse` | `CrestCreates.AspNetCore.Errors` | Public JSON error response: `Code`, `Message`, `Details`, `TraceId`, `StatusCode`. |
| `CrestExceptionConversionResult` | `CrestCreates.AspNetCore.Errors` | Internal result from exception conversion: response body, log level, exception category. |
| `ICrestExceptionConverter` | `CrestCreates.AspNetCore.Errors` | Converts any exception into `CrestErrorResponse`. Performs type mapping, localization, and safe fallback. |
| `DefaultCrestExceptionConverter` | `CrestCreates.AspNetCore.Errors` | Default converter implementation. |
| `ExceptionHandlingMiddleware` | `CrestCreates.AspNetCore.Middlewares` | Catches exceptions, delegates conversion, logs, writes JSON. |
| DI extensions | `CrestCreates.AspNetCore` | Register converter and middleware support. |

The middleware should stay small. It should not contain a large `switch` with every exception case. The mapping belongs in `DefaultCrestExceptionConverter`, where it is easier to unit test.

## Error Response Contract

| Field | Type | Meaning |
|---|---|---|
| `code` | string | Stable error code, for example `Crest.Auth.Forbidden`. |
| `message` | string | Public, localized, user-safe message. |
| `details` | string? | Optional safe details. Never stack traces. Never connection strings. |
| `traceId` | string | `HttpContext.TraceIdentifier`. Used to correlate response, logs, and audit logs. |
| `statusCode` | int | HTTP status code. |

Serialization should use the app's configured JSON options when available. The response shape must be stable in tests.

## Exception Mapping

| Scenario | Exception | Error code | HTTP |
|---|---|---|---|
| Business rule failed | `CrestBusinessException` | Exception-provided code, for example `Tenant.AlreadyExists` | 400 |
| Validation failed | `CrestValidationException` | `Crest.Validation.Failed` | 400 |
| Not authenticated | `UnauthorizedAccessException` | `Crest.Auth.Unauthorized` | 401 |
| Forbidden | `CrestPermissionException` | `Crest.Auth.Forbidden` | 403 |
| Entity not found | `CrestEntityNotFoundException` / `KeyNotFoundException` | `Crest.Entity.NotFound` | 404 |
| Concurrency conflict | `CrestConcurrencyException` | `Crest.Concurrency.Conflict` | 409 |
| Missing precondition | `CrestPreconditionRequiredException` | `Crest.Concurrency.PreconditionRequired` | 428 |
| Invalid argument | `ArgumentException` | `Crest.Request.InvalidArgument` | 400 |
| Invalid operation | `InvalidOperationException` | `Crest.Operation.Invalid` | 400 |
| Unhandled exception | Other `Exception` | `Crest.InternalError` | 500 |

Mapping rules:

| Rule | Requirement |
|---|---|
| Type-based mapping only | Do not inspect exception messages with `Contains`, regex, or similar logic. |
| `CrestException` first | If an exception derives from `CrestException`, use its `ErrorCode`, `HttpStatusCode`, and safe details. |
| Existing exceptions align | Existing concurrency and precondition exceptions should inherit from the platform exception base instead of remaining isolated `Exception` types. |
| 500 is safe | 500 responses must not include raw exception messages, stack traces, SQL, connection strings, or provider-specific infrastructure details. |
| Known 4xx may include safe details | Details can be included only when the exception type explicitly carries safe details. |

## Localization

Public message resolution uses `CrestCreates.Localization.Services.ILocalizationService`.

Lookup order:

| Step | Behavior |
|---|---|
| 1 | Try localized text by `ErrorCode`. |
| 2 | If the localized value exists and is not just the key, use it as `message`. |
| 3 | Otherwise use the exception's safe fallback message. |
| 4 | Otherwise use the converter's default safe message for the HTTP status. |

Initial resource keys should include:

| Error code | Default Chinese message |
|---|---|
| `Crest.Auth.Unauthorized` | `当前请求未认证。` |
| `Crest.Auth.Forbidden` | `没有权限执行当前操作。` |
| `Crest.Validation.Failed` | `数据验证失败。` |
| `Crest.Request.InvalidArgument` | `请求参数错误。` |
| `Crest.Operation.Invalid` | `当前操作无效。` |
| `Crest.Entity.NotFound` | `资源不存在。` |
| `Crest.Concurrency.Conflict` | `数据已被其他用户修改，请刷新后重试。` |
| `Crest.Concurrency.PreconditionRequired` | `请求缺少 If-Match 头，请提供当前 ConcurrencyStamp。` |
| `Crest.InternalError` | `服务器内部错误。` |

Localization failure must never break exception handling. If localization throws, the converter uses the safe fallback and logs the localization failure at warning level.

## Request Flow

The intended Web pipeline remains:

```text
Request
  -> RequestLogging
  -> ExceptionHandlingMiddleware
  -> AuditLogging
  -> Routing
  -> MultiTenancy
  -> Authentication
  -> TenantBoundary
  -> Authorization
  -> MVC / Generated Dynamic API / Generated CRUD
```

Behavior by source:

| Source | Behavior |
|---|---|
| MVC controller | Throws exceptions normally; middleware writes `CrestErrorResponse`. |
| Generated Dynamic API | Generated endpoint should not catch and rewrite errors. It should continue throwing into middleware. |
| Generated CRUD / `CrudControllerBase` | Missing `If-Match` and concurrency conflicts throw platform exceptions and are converted by middleware. |
| `CrestAppServiceBase` | Must rethrow platform exceptions and known authorization/not-found exceptions before generic wrapping. |
| Authentication / authorization | Unauthorized and forbidden failures must produce the same response contract where they flow through exceptions. Middleware cannot rewrite framework-produced `ChallengeResult` / `ForbidResult` responses unless a later explicit result handler is designed. |

## App Service Exception Policy

`CrestAppServiceBase` should not wrap known platform exceptions into generic `Exception`.

Rethrow whitelist:

| Exception type | Why |
|---|---|
| `CrestException` | Already carries platform error semantics. |
| `CrestPermissionException` | Permission failure should remain 403. If it is moved under `CrestException`, this is covered by the first rule. |
| `UnauthorizedAccessException` | Should remain 401. |
| `KeyNotFoundException` | Should map to 404. |
| `OperationCanceledException` | Should propagate cancellation, not become a 500 response. |

For database exceptions and unknown exceptions, the service can continue wrapping with a generic message, but the middleware must treat the final unknown exception as 500 and hide internals from the response.

## Logging And Audit

| Area | Design |
|---|---|
| Logging level | 4xx known errors log as warning; 500 logs as error. |
| Log fields | Include `traceId`, `errorCode`, `statusCode`, and request path when available. |
| Response safety | Response body never includes stack trace. |
| Audit chain | Do not replace audit logging. `AuditLoggingMiddleware` already records exception context and should continue to see the thrown exception before the exception middleware writes the response. |
| Correlation | Response, logs, and audit use `HttpContext.TraceIdentifier`. |

## Legacy Middleware Cleanup

Delete:

```text
framework/src/CrestCreates.Web/Middlewares/ExceptionHandlingMiddleware.cs
```

Then remove the redundant `Compile Remove="Middlewares\ExceptionHandlingMiddleware.cs"` entry from `framework/src/CrestCreates.Web/CrestCreates.Web.csproj` if it becomes unnecessary.

Acceptance for cleanup:

| Check | Expected |
|---|---|
| Search for `class ExceptionHandlingMiddleware` | Only the AspNetCore implementation remains. |
| `UseExceptionHandling` extension | Comes from `CrestCreates.AspNetCore.Middlewares`. |
| Tests | Existing middleware tests target the formal AspNetCore type. |

## Testing Strategy

| Test | Layer | Verifies |
|---|---|---|
| `BusinessException_ShouldReturnExpectedErrorCode` | AspNetCore unit | `CrestBusinessException("Tenant.AlreadyExists")` returns 400 and `code=Tenant.AlreadyExists`. |
| `ValidationException_ShouldReturnValidationError` | AspNetCore unit | Validation returns 400 and `code=Crest.Validation.Failed`. |
| `PermissionException_ShouldReturn403` | AspNetCore unit | Permission exception returns 403 and `code=Crest.Auth.Forbidden`. |
| `UnauthorizedException_ShouldReturn401` | AspNetCore unit | Unauthorized exception returns 401. |
| `ConcurrencyException_ShouldReturn409` | AspNetCore unit / integration | Concurrency conflict returns 409 and stable code. |
| `PreconditionRequiredException_ShouldReturn428` | AspNetCore unit / integration | Missing `If-Match` returns 428 and stable code. |
| `EntityNotFoundException_ShouldReturn404` | AspNetCore unit | Entity not found returns 404. |
| `UnhandledException_ShouldReturn500WithoutLeakingStackTrace` | AspNetCore unit | Response hides raw exception details. |
| `LocalizedErrorCode_ShouldUseLocalizedMessage` | AspNetCore + localization unit | `ErrorCode` resolves localized message. |
| `MissingLocalization_ShouldFallbackToSafeMessage` | AspNetCore unit | Missing resource still returns safe message. |
| `DynamicApiException_ShouldUseSameErrorEnvelope` | Integration | Generated Dynamic API errors use the same response shape. |
| `GeneratedCrudDelete_WithoutIfMatch_ShouldReturn428Envelope` | Integration | Generated CRUD or MVC CRUD missing `If-Match` uses unified envelope. |
| `ErrorResponse_ShouldContainTraceIdAndStatusCode` | Integration | Response includes `traceId`, `statusCode`, and stable `code`. |

Recommended test placement:

| Project | Tests |
|---|---|
| `CrestCreates.Web.Tests` or new `CrestCreates.AspNetCore.Tests` | Converter and middleware unit tests. |
| `CrestCreates.IntegrationTests` | Web pipeline, generated Dynamic API, and generated CRUD behavior. |
| `CrestCreates.Localization.Tests` | Only localization fallback gaps not already covered by existing localization tests. |

## Acceptance Criteria

| Criterion | Expected result |
|---|---|
| 401/403/409/428 mapping | Unauthorized, forbidden, concurrency, and precondition failures return correct HTTP status and stable error code. |
| Business exceptions | Business exceptions are not wrapped as 500. |
| Response consistency | Web, generated Dynamic API, and generated CRUD errors use the same `{ code, message, details, traceId, statusCode }` shape. |
| Localization | Messages are localized by `ErrorCode` when resources exist, with safe fallback when missing. |
| Traceability | Every error response includes `traceId`; logs use the same trace id. |
| Safety | 500 responses do not leak stack traces or infrastructure details. |
| Mainline cleanup | Excluded Web middleware copy is removed. |

## Implementation Notes

| Topic | Guidance |
|---|---|
| Namespace migration | Avoid breaking consumers unnecessarily. Existing `CrestCreates.Domain.Exceptions.CrestConcurrencyException` can remain in its namespace while inheriting from `CrestException`. |
| Authorization exception duplication | There are permission exception types in authorization-related namespaces. Pick one formal type and add forwarding compatibility only if existing references require it. |
| Generated API path | Do not add generated catch blocks. The generated path should remain simple and let the middleware own error output. |
| JSON options | Prefer ASP.NET Core configured JSON options instead of ad hoc `JsonSerializer.Serialize` defaults. |
| AoT | If source-generated JSON contexts are already used in the target project, add one for `CrestErrorResponse`; otherwise keep the response DTO simple and avoid reflection-heavy customization. |

