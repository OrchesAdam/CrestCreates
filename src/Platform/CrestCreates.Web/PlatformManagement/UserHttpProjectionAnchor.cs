using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.PlatformManagement;

[CapabilityCompatibilityProjection(
    ContractType = typeof(IUserAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/users",
    CapabilityIdPrefix = "compat.appservice.user")]
public static class UserHttpProjectionAnchor;
