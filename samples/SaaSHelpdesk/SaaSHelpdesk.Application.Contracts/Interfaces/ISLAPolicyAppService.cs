using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ISLAPolicyAppService
{
    Task<SLAPolicyDto> CreateAsync(CreateSLAPolicyDto input, CancellationToken cancellationToken = default);
    Task<SLAPolicyDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SLAPolicyDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SLAPolicyDto> UpdateAsync(Guid id, UpdateSLAPolicyDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}
