using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class Bom : AuditedEntity<Guid>
{
    public Guid MaterialId { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public BomStatus Status { get; private set; } = BomStatus.Draft;
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<BomLine> Lines { get; private set; } = new List<BomLine>();

    protected Bom() { }

    public Bom(Guid id, Guid materialId, string version, Guid tenantId)
    {
        Id = id;
        MaterialId = materialId;
        SetVersion(version);
        TenantId = tenantId;
    }

    public void SetVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 32)
            throw new ArgumentException("Version must be between 1 and 32 characters", nameof(version));
        Version = version;
    }

    public void SetNotes(string? notes) => Notes = notes;

    public void Activate()
    {
        if (Lines.Count == 0)
            throw new InvalidOperationException("A BOM without lines cannot be activated.");
        Status = BomStatus.Active;
    }

    public void Disable() => Status = BomStatus.Disabled;

    public void ReplaceLines(IEnumerable<BomLine> lines)
    {
        if (Status == BomStatus.Active)
            throw new InvalidOperationException("An active BOM cannot be edited; disable it first.");
        Lines.Clear();
        foreach (var line in lines)
            Lines.Add(line);
    }
}