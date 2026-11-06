using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.AuditRetention;

/// <summary>
/// Explicit-host HTTP adapter over the real audit retention admission contract. Hosts enable it
/// through <see cref="AuditRetentionHttpProjectionExtensions.AddCrestAuditRetentionHttp"/> after
/// composing a retention store.
/// </summary>
[CapabilityCompatibilityProjection(
    ContractType = typeof(IAuditRetentionAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/audit-retention",
    CapabilityIdPrefix = "compat.appservice.audit-retention")]
public static class AuditRetentionHttpProjectionAnchor;
