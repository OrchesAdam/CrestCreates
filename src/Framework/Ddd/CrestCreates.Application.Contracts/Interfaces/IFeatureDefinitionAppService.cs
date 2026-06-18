using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IFeatureDefinitionAppService
{
    Task<List<FeatureDefinitionDto>> GetAllAsync();
    Task<FeatureDefinitionDto?> GetAsync(string name);
    Task<List<FeatureGroupDto>> GetGroupsAsync();
}
