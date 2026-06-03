using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class SLAPolicy : AuditedEntity<Guid>
{
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int LowPriorityResponseMinutes { get; private set; }
    public int LowPriorityResolutionMinutes { get; private set; }
    public int MediumPriorityResponseMinutes { get; private set; }
    public int MediumPriorityResolutionMinutes { get; private set; }
    public int HighPriorityResponseMinutes { get; private set; }
    public int HighPriorityResolutionMinutes { get; private set; }
    public int UrgentPriorityResponseMinutes { get; private set; }
    public int UrgentPriorityResolutionMinutes { get; private set; }

    protected SLAPolicy() { }

    public SLAPolicy(Guid id, string name)
    {
        Id = id;
        SetName(name);
        ConcurrencyStamp = Guid.NewGuid().ToString();
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));
        Name = name;
    }

    public void SetDescription(string? description)
    {
        Description = description;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void SetResponseMinutes(TicketPriority priority, int minutes)
    {
        if (minutes <= 0)
            throw new ArgumentException("Response minutes must be positive", nameof(minutes));

        switch (priority)
        {
            case TicketPriority.Low: LowPriorityResponseMinutes = minutes; break;
            case TicketPriority.Medium: MediumPriorityResponseMinutes = minutes; break;
            case TicketPriority.High: HighPriorityResponseMinutes = minutes; break;
            case TicketPriority.Urgent: UrgentPriorityResponseMinutes = minutes; break;
        }
    }

    public void SetResolutionMinutes(TicketPriority priority, int minutes)
    {
        if (minutes <= 0)
            throw new ArgumentException("Resolution minutes must be positive", nameof(minutes));

        switch (priority)
        {
            case TicketPriority.Low: LowPriorityResolutionMinutes = minutes; break;
            case TicketPriority.Medium: MediumPriorityResolutionMinutes = minutes; break;
            case TicketPriority.High: HighPriorityResolutionMinutes = minutes; break;
            case TicketPriority.Urgent: UrgentPriorityResolutionMinutes = minutes; break;
        }
    }

    public int GetResponseMinutes(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => LowPriorityResponseMinutes,
        TicketPriority.Medium => MediumPriorityResponseMinutes,
        TicketPriority.High => HighPriorityResponseMinutes,
        TicketPriority.Urgent => UrgentPriorityResponseMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(priority))
    };

    public int GetResolutionMinutes(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => LowPriorityResolutionMinutes,
        TicketPriority.Medium => MediumPriorityResolutionMinutes,
        TicketPriority.High => HighPriorityResolutionMinutes,
        TicketPriority.Urgent => UrgentPriorityResolutionMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(priority))
    };
}
