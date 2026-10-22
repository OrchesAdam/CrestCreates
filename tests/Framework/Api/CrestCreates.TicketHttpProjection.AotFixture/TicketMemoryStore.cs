using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace CrestCreates.TicketHttpProjection.AotFixture;

internal sealed record TicketRow(
    Guid Id,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    TicketType Type,
    Guid CustomerId,
    Guid? AssigneeId,
    Guid? CategoryId,
    DateTime? DueDate,
    DateTime? ResolvedAt,
    DateTime? ClosedAt,
    bool IsEscalated,
    DateTime CreationTime,
    Guid? CreatorId,
    DateTime? LastModificationTime,
    Guid? LastModifierId,
    string ConcurrencyStamp);

internal sealed record SlaRow(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int LowResponse,
    int LowResolution,
    int MediumResponse,
    int MediumResolution,
    int HighResponse,
    int HighResolution,
    int UrgentResponse,
    int UrgentResolution,
    string ConcurrencyStamp);

/// <summary>
/// The fixture's persistence leaf: one in-memory store for Ticket and the active SLA policy. It keeps
/// detached row images (never live entities), mirrors the provider's stamp issuance/rotation, and
/// exposes a write gate + snapshot/restore pair for the fixture's memory unit of work.
/// </summary>
public sealed class TicketMemoryStore
{
    private readonly object _sync = new();
    private readonly List<TicketRow> _tickets = new();
    private SlaRow? _sla;

    public SemaphoreSlim WriteGate { get; } = new(1, 1);

    internal object Sync => _sync;

    internal List<TicketRow> TicketRows
    {
        get
        {
            lock (_sync)
            {
                return _tickets.ToList();
            }
        }
    }

    internal SlaRow? SlaRow
    {
        get
        {
            lock (_sync)
            {
                return _sla;
            }
        }
    }

    public int TicketCount
    {
        get
        {
            lock (_sync)
            {
                return _tickets.Count;
            }
        }
    }

    public void SeedActiveSlaPolicy(int responseMinutes = 30)
    {
        var policy = new SLAPolicy(Guid.NewGuid(), "Native SLA");
        policy.SetResponseMinutes(TicketPriority.Low, responseMinutes);
        policy.SetResponseMinutes(TicketPriority.Medium, responseMinutes);
        policy.SetResponseMinutes(TicketPriority.High, responseMinutes);
        policy.SetResponseMinutes(TicketPriority.Urgent, responseMinutes);
        lock (_sync)
        {
            _sla = TicketRowsMapper.Capture(policy);
        }
    }

    internal (List<TicketRow> Tickets, SlaRow? Sla) Snapshot()
    {
        lock (_sync)
        {
            return (_tickets.ToList(), _sla);
        }
    }

    internal void Restore((List<TicketRow> Tickets, SlaRow? Sla) snapshot)
    {
        lock (_sync)
        {
            _tickets.Clear();
            _tickets.AddRange(snapshot.Tickets);
            _sla = snapshot.Sla;
        }
    }

    internal TicketRow? FindTicket(Guid id)
    {
        lock (_sync)
        {
            return _tickets.FirstOrDefault(row => row.Id == id);
        }
    }

    internal bool AnyTicket(Func<TicketRow, bool> predicate)
    {
        lock (_sync)
        {
            return _tickets.Any(predicate);
        }
    }

    internal List<TicketRow> FindTickets(Func<TicketRow, bool> predicate)
    {
        lock (_sync)
        {
            return _tickets.Where(predicate).ToList();
        }
    }

    internal void InsertTicket(TicketRow row)
    {
        lock (_sync)
        {
            _tickets.Add(row);
        }
    }

    internal void ReplaceTicket(TicketRow row)
    {
        lock (_sync)
        {
            var index = _tickets.FindIndex(existing => existing.Id == row.Id);
            if (index < 0)
            {
                throw new InvalidOperationException($"Ticket {row.Id} disappeared from the native store.");
            }

            _tickets[index] = row;
        }
    }

    internal bool RemoveTicket(Guid id, string? expectedStamp, bool requireStamp)
    {
        lock (_sync)
        {
            var index = _tickets.FindIndex(row => row.Id == id);
            if (index < 0)
            {
                return false;
            }

            if (requireStamp && !string.Equals(_tickets[index].ConcurrencyStamp, expectedStamp, StringComparison.Ordinal))
            {
                throw new CrestCreates.Domain.Exceptions.CrestConcurrencyException("Ticket", id);
            }

            _tickets.RemoveAt(index);
            return true;
        }
    }

}

internal static class TicketRowsMapper
{
    public static Ticket ToTicket(TicketRow row)
    {
        var ticket = new Ticket(row.Id, row.Title, row.Description, row.Priority, row.Type, row.CustomerId);
        ticket.SetDueDate(row.DueDate);
        ticket.SetCategory(row.CategoryId);

        if (row.AssigneeId.HasValue)
        {
            ticket.AssignTo(row.AssigneeId.Value);
        }

        if (row.Status == TicketStatus.Resolved)
        {
            ticket.Resolve();
        }
        else if (row.Status == TicketStatus.Closed)
        {
            ticket.Close();
        }

        if (row.IsEscalated)
        {
            ticket.Escalate();
        }

        ticket.CreationTime = row.CreationTime;
        ticket.CreatorId = row.CreatorId;
        ticket.LastModificationTime = row.LastModificationTime;
        ticket.LastModifierId = row.LastModifierId;
        ticket.ConcurrencyStamp = row.ConcurrencyStamp;
        return ticket;
    }

    public static TicketRow Capture(Ticket ticket)
        => new(
            ticket.Id,
            ticket.Title,
            ticket.Description,
            ticket.Status,
            ticket.Priority,
            ticket.Type,
            ticket.CustomerId,
            ticket.AssigneeId,
            ticket.CategoryId,
            ticket.DueDate,
            ticket.ResolvedAt,
            ticket.ClosedAt,
            ticket.IsEscalated,
            ticket.CreationTime,
            ticket.CreatorId,
            ticket.LastModificationTime,
            ticket.LastModifierId,
            ticket.ConcurrencyStamp);

    public static SLAPolicy ToSlaPolicy(SlaRow row)
    {
        var policy = new SLAPolicy(row.Id, row.Name);
        policy.SetDescription(row.Description);
        if (!row.IsActive)
        {
            policy.Deactivate();
        }

        Set(policy, TicketPriority.Low, row.LowResponse, row.LowResolution);
        Set(policy, TicketPriority.Medium, row.MediumResponse, row.MediumResolution);
        Set(policy, TicketPriority.High, row.HighResponse, row.HighResolution);
        Set(policy, TicketPriority.Urgent, row.UrgentResponse, row.UrgentResolution);
        policy.ConcurrencyStamp = row.ConcurrencyStamp;
        return policy;
    }

    public static SlaRow Capture(SLAPolicy policy)
        => new(
            policy.Id,
            policy.Name,
            policy.Description,
            policy.IsActive,
            policy.LowPriorityResponseMinutes,
            policy.LowPriorityResolutionMinutes,
            policy.MediumPriorityResponseMinutes,
            policy.MediumPriorityResolutionMinutes,
            policy.HighPriorityResponseMinutes,
            policy.HighPriorityResolutionMinutes,
            policy.UrgentPriorityResponseMinutes,
            policy.UrgentPriorityResolutionMinutes,
            policy.ConcurrencyStamp);

    private static void Set(SLAPolicy policy, TicketPriority priority, int response, int resolution)
    {
        if (response > 0)
        {
            policy.SetResponseMinutes(priority, response);
        }

        if (resolution > 0)
        {
            policy.SetResolutionMinutes(priority, resolution);
        }
    }
}
