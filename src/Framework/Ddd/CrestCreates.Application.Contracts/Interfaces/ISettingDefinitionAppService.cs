using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ISettingDefinitionAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "groups", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Settings.Read })]
    Task<IReadOnlyList<SettingGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Settings.Read })]
    Task<IReadOnlyList<SettingDefinitionDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{name}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Settings.Read })]
    Task<SettingDefinitionDto?> GetAsync(string name, CancellationToken cancellationToken = default);
}