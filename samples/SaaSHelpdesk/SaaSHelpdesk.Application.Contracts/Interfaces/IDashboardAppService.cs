using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface IDashboardAppService
{
    Task<DashboardSummaryDto> GetSummaryAsync();
}
