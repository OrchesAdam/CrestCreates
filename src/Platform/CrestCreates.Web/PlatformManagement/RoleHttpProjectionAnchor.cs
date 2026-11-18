using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.PlatformManagement;

[CapabilityCompatibilityProjection(
    ContractType = typeof(IRoleAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/roles",
    CapabilityIdPrefix = "compat.appservice.role")]
public static class RoleHttpProjectionAnchor;
