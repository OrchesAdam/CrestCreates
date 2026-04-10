using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Settings;

[CrestService]
public class SettingDefinitionAppService : ISettingDefinitionAppService
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly SettingDefinitionAppServiceMapper _mapper;

    public SettingDefinitionAppService(
        ISettingDefinitionManager settingDefinitionManager,
        SettingDefinitionAppServiceMapper mapper)
    {
        _settingDefinitionManager = settingDefinitionManager;
        _mapper = mapper;
    }

    public Task<IReadOnlyList<SettingGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        var result = _settingDefinitionManager.GetGroups()
            .Select(_mapper.Map)
            .ToArray();

        return Task.FromResult<IReadOnlyList<SettingGroupDto>>(result);
    }

    public Task<IReadOnlyList<SettingDefinitionDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var result = _settingDefinitionManager.GetAll()
            .Select(_mapper.Map)
            .ToArray();

        return Task.FromResult<IReadOnlyList<SettingDefinitionDto>>(result);
    }

    public Task<SettingDefinitionDto?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var definition = _settingDefinitionManager.GetOrNull(name);
        return Task.FromResult(definition is null ? null : _mapper.Map(definition));
    }
}
