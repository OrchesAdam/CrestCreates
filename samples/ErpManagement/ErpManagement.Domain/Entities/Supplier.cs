using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class Supplier : AuditedEntity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public int PaymentTermsDays { get; private set; } = 30;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    protected Supplier() { }

    public Supplier(Guid id, string code, string name, Guid tenantId)
    {
        Id = id;
        SetCode(code);
        SetName(name);
        TenantId = tenantId;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
            throw new ArgumentException("Code must be between 1 and 64 characters", nameof(code));
        Code = code;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("Name must be between 1 and 200 characters", nameof(name));
        Name = name;
    }

    public void SetContactName(string? contactName) => ContactName = contactName;
    public void SetPhone(string? phone) => Phone = phone;
    public void SetEmail(string? email) => Email = email;
    public void SetAddress(string? address) => Address = address;

    public void SetPaymentTermsDays(int days)
    {
        if (days < 0)
            throw new ArgumentException("Payment terms cannot be negative", nameof(days));
        PaymentTermsDays = days;
    }

    public void SetNotes(string? notes) => Notes = notes;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}