using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Web.PlatformManagement;

[CapabilityCompatibilityProjection(
    ContractType = typeof(IFeatureAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/features",
    CapabilityIdPrefix = "compat.appservice.feature")]
public static class FeatureHttpProjectionAnchor;
