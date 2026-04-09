using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IRoleAppService
{
    Task<IdentityRoleDto> CreateAsync(CreateIdentityRoleDto input, CancellationToken cancellationToken = default);
    Task<IdentityRoleDto?> GetAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IdentityRoleDto>> GetListAsync(string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IdentityRoleDto> UpdateAsync(Guid roleId, UpdateIdentityRoleDto input, CancellationToken cancellationToken = default);
    Task SetActiveAsync(Guid roleId, bool isActive, CancellationToken cancellationToken = default);
}
