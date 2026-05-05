using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Features;
using CrestPermissionException = CrestCreates.Domain.Exceptions.CrestPermissionException;

namespace CrestCreates.Application.Features;

[CrestService]
public class FeatureDefinitionAppService : IFeatureDefinitionAppService
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly IPermissionChecker _permissionChecker;

    public FeatureDefinitionAppService(
        IFeatureDefinitionManager featureDefinitionManager,
        IPermissionChecker permissionChecker)
    {
        _featureDefinitionManager = featureDefinitionManager;
        _permissionChecker = permissionChecker;
    }

    public async Task<List<FeatureDefinitionDto>> GetAllAsync()
    {
        await EnsureGrantedAsync(FeatureManagementPermissions.Read);
        var definitions = _featureDefinitionManager.GetAll();
        var dtos = new List<FeatureDefinitionDto>();

        foreach (var definition in definitions)
        {
            dtos.Add(MapToDto(definition));
        }

        return dtos;
    }

    public async Task<FeatureDefinitionDto?> GetAsync(string name)
    {
        await EnsureGrantedAsync(FeatureManagementPermissions.Read);
        var definition = _featureDefinitionManager.GetOrNull(name);
        if (definition == null)
        {
            return null;
        }

        return MapToDto(definition);
    }

    public async Task<List<FeatureGroupDto>> GetGroupsAsync()
    {
        await EnsureGrantedAsync(FeatureManagementPermissions.Read);
        var groups = _featureDefinitionManager.GetGroups();
        var dtos = new List<FeatureGroupDto>();

        foreach (var group in groups)
        {
            dtos.Add(new FeatureGroupDto
            {
                Name = group.Name,
                DisplayName = group.DisplayName,
                Description = group.Description
            });
        }

        return dtos;
    }

    private async Task EnsureGrantedAsync(string permission)
    {
        if (!await _permissionChecker.IsGrantedAsync(permission))
        {
            throw new CrestPermissionException(permission);
        }
    }

    private static FeatureDefinitionDto MapToDto(FeatureDefinition definition)
    {
        return new FeatureDefinitionDto
        {
            Name = definition.Name,
            GroupName = definition.GroupName,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            DefaultValue = definition.DefaultValue,
            ValueType = definition.ValueType,
            IsVisible = definition.IsVisible,
            IsEditable = definition.IsEditable,
            Scopes = definition.Scopes
        };
    }
}
