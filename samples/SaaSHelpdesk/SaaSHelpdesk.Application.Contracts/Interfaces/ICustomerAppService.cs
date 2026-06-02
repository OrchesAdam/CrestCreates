using System;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICustomerAppService
{
    Task<CustomerDto> ActivateAsync(Guid id);
    Task<CustomerDto> DeactivateAsync(Guid id);
}
