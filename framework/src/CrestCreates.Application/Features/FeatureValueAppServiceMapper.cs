using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Domain.Features;

namespace CrestCreates.Application.Features;

public class FeatureValueAppServiceMapper
{
    public FeatureValueDto Map(ResolvedFeatureValue value)
    {
        return new FeatureValueDto
        {
            Name = value.Name,
            Value = value.Value,
            Scope = value.Scope,
            ProviderKey = value.ProviderKey,
            TenantId = value.TenantId
        };
    }

    public FeatureValueDto Map(FeatureValueEntry value)
    {
        return new FeatureValueDto
        {
            Name = value.Name,
            Value = value.Value,
            Scope = value.Scope,
            ProviderKey = value.ProviderKey,
            TenantId = value.TenantId
        };
    }
}
