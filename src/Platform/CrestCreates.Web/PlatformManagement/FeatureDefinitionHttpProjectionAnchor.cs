using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.PlatformManagement;

[CapabilityCompatibilityProjection(
    ContractType = typeof(IFeatureDefinitionAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/feature-definitions",
    CapabilityIdPrefix = "compat.appservice.feature-definition")]
public static class FeatureDefinitionHttpProjectionAnchor;
