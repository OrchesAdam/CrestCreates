using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IFeatureDefinitionAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<List<FeatureDefinitionDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{name}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<FeatureDefinitionDto?> GetAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "groups", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<List<FeatureGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);
}