using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace SaaSHelpdesk.Domain.Entities;

[Entity]
public class Customer : AuditedEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Company { get; private set; }
    public Guid TenantId { get; private set; }
    public string? ApiKey { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    // Navigation
    public virtual ICollection<Ticket> Tickets { get; private set; } = new HashSet<Ticket>();

    protected Customer() { }

    public Customer(Guid id, string name, string email, Guid tenantId)
    {
        Id = id;
        SetName(name);
        SetEmail(email);
        TenantId = tenantId;
        ApiKey = Guid.NewGuid().ToString("N");
        IsActive = true;
    }

    public void SetPhone(string? phone)
    {
        Phone = phone;
    }

    public void SetCompany(string? company)
    {
        Company = company;
    }

    public void SetNotes(string? notes)
    {
        Notes = notes;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void RefreshApiKey()
    {
        ApiKey = Guid.NewGuid().ToString("N");
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new ArgumentException("Name must be between 1 and 100 characters", nameof(name));
        Name = name;
    }

    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
            throw new ArgumentException("Email is invalid", nameof(email));
        Email = email;
    }
}
