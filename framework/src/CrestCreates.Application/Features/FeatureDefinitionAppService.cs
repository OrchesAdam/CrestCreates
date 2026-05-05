using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

[CrestService]
public class FeatureDefinitionAppService : IFeatureDefinitionAppService
{
    private readonly IFeatureDefinitionManager _featureDefinitionManager;

    public FeatureDefinitionAppService(IFeatureDefinitionManager featureDefinitionManager)
    {
        _featureDefinitionManager = featureDefinitionManager;
    }

    public Task<List<FeatureDefinitionDto>> GetAllAsync()
    {
        var definitions = _featureDefinitionManager.GetAll();
        var dtos = new List<FeatureDefinitionDto>();

        foreach (var definition in definitions)
        {
            dtos.Add(MapToDto(definition));
        }

        return Task.FromResult(dtos);
    }

    public Task<FeatureDefinitionDto?> GetAsync(string name)
    {
        var definition = _featureDefinitionManager.GetOrNull(name);
        if (definition == null)
        {
            return Task.FromResult<FeatureDefinitionDto?>(null);
        }

        return Task.FromResult<FeatureDefinitionDto?>(MapToDto(definition));
    }

    public Task<List<FeatureGroupDto>> GetGroupsAsync()
    {
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

        return Task.FromResult(dtos);
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
