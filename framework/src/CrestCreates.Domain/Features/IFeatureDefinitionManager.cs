using System.Collections.Generic;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public interface IFeatureDefinitionManager
{
    IReadOnlyList<FeatureDefinitionGroup> GetGroups();

    IReadOnlyList<FeatureDefinition> GetAll();

    FeatureDefinition? GetOrNull(string name);
}
