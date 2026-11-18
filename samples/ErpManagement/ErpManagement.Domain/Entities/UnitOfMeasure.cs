using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class UnitOfMeasure : AuditedEntity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Precision { get; private set; } = 2;
    public Guid TenantId { get; private set; }

    protected UnitOfMeasure() { }

    public UnitOfMeasure(Guid id, string code, string name, Guid tenantId)
    {
        Id = id;
        SetCode(code);
        SetName(name);
        TenantId = tenantId;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 32)
            throw new ArgumentException("Code must be between 1 and 32 characters", nameof(code));
        Code = code;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new ArgumentException("Name must be between 1 and 100 characters", nameof(name));
        Name = name;
    }

    public void SetPrecision(int precision)
    {
        if (precision is < 0 or > 6)
            throw new ArgumentException("Precision must be between 0 and 6", nameof(precision));
        Precision = precision;
    }
}