namespace CrestCreates.Domain.Shared.Entities.Auditing
{
    public interface IMustHaveTenant
    {
        string TenantId { get; set; }
    }
}
