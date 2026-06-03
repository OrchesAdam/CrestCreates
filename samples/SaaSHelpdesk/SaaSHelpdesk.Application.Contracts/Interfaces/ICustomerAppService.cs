using System;
using System.Threading;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICustomerAppService
{
    Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
    Task<CustomerDto> ActivateAsync(Guid id);
    Task<CustomerDto> DeactivateAsync(Guid id);
}
