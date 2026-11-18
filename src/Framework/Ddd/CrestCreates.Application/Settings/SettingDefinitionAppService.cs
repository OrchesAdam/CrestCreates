using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Settings;

[CrestService]
public class SettingDefinitionAppService : ISettingDefinitionAppService
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly SettingDefinitionAppServiceMapper _mapper;
    private readonly IPermissionChecker _permissionChecker;

    public SettingDefinitionAppService(
        ISettingDefinitionManager settingDefinitionManager,
        SettingDefinitionAppServiceMapper mapper,
        IPermissionChecker permissionChecker)
    {
        _settingDefinitionManager = settingDefinitionManager;
        _mapper = mapper;
        _permissionChecker = permissionChecker;
    }

    public async Task<IReadOnlyList<SettingGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(SettingManagementPermissions.Read);
        var result = _settingDefinitionManager.GetGroups()
            .Select(_mapper.Map)
            .ToArray();

        return result;
    }

    public async Task<IReadOnlyList<SettingDefinitionDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(SettingManagementPermissions.Read);
        var result = _settingDefinitionManager.GetAll()
            .Select(_mapper.Map)
            .ToArray();

        return result;
    }

    public async Task<SettingDefinitionDto?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(SettingManagementPermissions.Read);
        var definition = _settingDefinitionManager.GetOrNull(name);
        return definition is null ? null : _mapper.Map(definition);
    }
}