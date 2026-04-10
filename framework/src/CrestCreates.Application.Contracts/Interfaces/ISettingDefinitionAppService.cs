using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ISettingDefinitionAppService
{
    Task<IReadOnlyList<SettingGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingDefinitionDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<SettingDefinitionDto?> GetAsync(string name, CancellationToken cancellationToken = default);
}
