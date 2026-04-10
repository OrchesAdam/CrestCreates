using System.Collections.Generic;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public interface ISettingDefinitionManager
{
    IReadOnlyList<SettingDefinitionGroup> GetGroups();

    IReadOnlyList<SettingDefinition> GetAll();

    SettingDefinition? GetOrNull(string name);
}
