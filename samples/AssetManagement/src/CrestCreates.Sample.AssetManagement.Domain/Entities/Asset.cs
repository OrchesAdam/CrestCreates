using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Sample.AssetManagement.Contracts;

namespace CrestCreates.Sample.AssetManagement.Domain.Entities;

[Entity]
public sealed class Asset : MustHaveTenantOrganizationEntity<Guid>
{
    public string AssetTag { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public AssetStatus Status { get; private set; }
    public string? AssignedUserId { get; private set; }
    public Guid? ActiveAssignmentId { get; private set; }
    public string? MaintenanceWorkflowInstanceId { get; private set; }

    private Asset() { }

    public Asset(
        Guid id,
        string tenantId,
        string assetTag,
        string name,
        string description,
        string category,
        Guid? organizationId,
        string? location,
        string creatorId)
    {
        Id = id;
        TenantId = Require(tenantId, nameof(tenantId), 100);
        AssetTag = ValidateAssetTag(assetTag);
        SetDetails(name, description, category, location);
        OrganizationId = organizationId;
        CreatorId = Guid.TryParse(creatorId, out var parsed) ? parsed : null;
        CreationTime = DateTime.UtcNow;
        Status = AssetStatus.Available;
    }

    public static Asset Rehydrate(
        Guid id,
        string tenantId,
        string assetTag,
        string name,
        string description,
        string category,
        Guid? organizationId,
        string? location,
        AssetStatus status,
        string? assignedUserId,
        Guid? activeAssignmentId,
        string? maintenanceWorkflowInstanceId,
        string concurrencyStamp,
        DateTime creationTime,
        DateTime? lastModificationTime,
        Guid? creatorId,
        Guid? lastModifierId)
    {
        var asset = new Asset
        {
            Id = id,
            TenantId = tenantId,
            AssetTag = assetTag,
            Name = name,
            Description = description,
            Category = category,
            OrganizationId = organizationId,
            Location = location,
            Status = status,
            AssignedUserId = assignedUserId,
            ActiveAssignmentId = activeAssignmentId,
            MaintenanceWorkflowInstanceId = maintenanceWorkflowInstanceId,
            ConcurrencyStamp = concurrencyStamp,
            CreationTime = creationTime,
            LastModificationTime = lastModificationTime,
            CreatorId = creatorId,
            LastModifierId = lastModifierId
        };
        return asset;
    }

    public void UpdateDetails(string name, string description, string category, string? location, string modifierId)
    {
        EnsureEditable();
        SetDetails(name, description, category, location);
        Touch(modifierId);
    }

    public AssetAssignment Assign(string userId, Guid organizationId, string modifierId)
    {
        Require(userId, nameof(userId), 200);
        if (OrganizationId != organizationId)
            throw Invalid("An asset can only be assigned inside its owning organization.");
        if (Status != AssetStatus.Available)
            throw Invalid($"Asset '{AssetTag}' cannot be assigned from status '{Status}'.");
        var assignment = new AssetAssignment(Guid.NewGuid(), TenantId, Id, userId, organizationId);
        AssignedUserId = userId;
        ActiveAssignmentId = assignment.Id;
        Status = AssetStatus.Assigned;
        Touch(modifierId);
        return assignment;
    }

    public void Return(string modifierId)
    {
        if (Status == AssetStatus.Available)
            return;
        if (Status != AssetStatus.Assigned || ActiveAssignmentId is null)
            throw Invalid($"Asset '{AssetTag}' cannot be returned from status '{Status}'.");
        Status = AssetStatus.Available;
        AssignedUserId = null;
        ActiveAssignmentId = null;
        Touch(modifierId);
    }

    public void Transfer(Guid organizationId, string? location, string modifierId)
    {
        if (Status is AssetStatus.Assigned or AssetStatus.MaintenancePending or AssetStatus.Retired)
            throw Invalid($"Asset '{AssetTag}' cannot be transferred from status '{Status}'.");
        OrganizationId = organizationId;
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        Touch(modifierId);
    }

    public void RequestMaintenance(string workflowInstanceId, string modifierId)
    {
        Require(workflowInstanceId, nameof(workflowInstanceId), 200);
        if (Status == AssetStatus.Retired || Status == AssetStatus.MaintenancePending)
            throw Invalid($"Asset '{AssetTag}' cannot enter maintenance from status '{Status}'.");
        Status = AssetStatus.MaintenancePending;
        MaintenanceWorkflowInstanceId = workflowInstanceId;
        Touch(modifierId);
    }

    public void ApplyMaintenanceDecision(bool approved, string workflowInstanceId, string modifierId)
    {
        if (Status != AssetStatus.MaintenancePending
            || !string.Equals(MaintenanceWorkflowInstanceId, workflowInstanceId, StringComparison.Ordinal))
            throw Invalid("The maintenance decision does not match the active asset maintenance request.");
        // Maintenance does not implicitly return an assigned asset. The
        // assignment remains authoritative throughout the review.
        Status = AssignedUserId is null ? AssetStatus.Available : AssetStatus.Assigned;
        MaintenanceWorkflowInstanceId = null;
        Touch(modifierId);
    }

    public void SetConcurrencyStamp(string stamp) => ConcurrencyStamp = Require(stamp, nameof(stamp), 200);

    private void EnsureEditable()
    {
        if (Status == AssetStatus.Retired)
            throw Invalid($"Asset '{AssetTag}' is retired and cannot be edited.");
    }

    private void SetDetails(string name, string description, string category, string? location)
    {
        Name = Require(name, nameof(name), 200);
        Description = Require(description, nameof(description), 2000);
        Category = Require(category, nameof(category), 100);
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
    }

    private void Touch(string modifierId)
    {
        LastModificationTime = DateTime.UtcNow;
        LastModifierId = Guid.TryParse(modifierId, out var parsed) ? parsed : null;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    private static string ValidateAssetTag(string value)
    {
        var tag = Require(value, nameof(value), 100);
        if (tag.Any(char.IsWhiteSpace))
            throw new ArgumentException("AssetTag cannot contain whitespace.", nameof(value));
        return tag;
    }

    private static string Require(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
            throw new ArgumentException($"{name} is required and must be at most {max} characters.", name);
        return value.Trim();
    }

    private static InvalidOperationException Invalid(string message) => new(message);
}
