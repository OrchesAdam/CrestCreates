using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.PlatformManagement;

[CapabilityCompatibilityProjection(
    ContractType = typeof(ISettingDefinitionAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/setting-definitions",
    CapabilityIdPrefix = "compat.appservice.setting-definition")]
public static class SettingDefinitionHttpProjectionAnchor;
