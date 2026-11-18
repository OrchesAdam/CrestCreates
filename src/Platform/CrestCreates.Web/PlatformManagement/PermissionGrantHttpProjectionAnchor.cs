using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.PlatformManagement;

[CapabilityCompatibilityProjection(
    ContractType = typeof(IPermissionGrantAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/permission-grants",
    CapabilityIdPrefix = "compat.appservice.permission-grant")]
public static class PermissionGrantHttpProjectionAnchor;
