using System.Collections.Generic;

namespace SaaSHelpdesk.Application.Contracts.DTOs;

public class DashboardSummaryDto
{
    public int TotalTickets { get; set; }
    public int OpenTickets { get; set; }
    public int InProgressTickets { get; set; }
    public int ResolvedTickets { get; set; }
    public int ClosedTickets { get; set; }
    public int OverdueTickets { get; set; }
    public int TicketsCreatedToday { get; set; }
    public double AverageResolutionHours { get; set; }
    public Dictionary<string, int> TicketsByPriority { get; set; } = new();
    public List<AgentWorkloadDto> AgentWorkloads { get; set; } = new();
}

public class AgentWorkloadDto
{
    public string AgentName { get; set; } = string.Empty;
    public int AssignedTickets { get; set; }
    public int ResolvedTickets { get; set; }
}
