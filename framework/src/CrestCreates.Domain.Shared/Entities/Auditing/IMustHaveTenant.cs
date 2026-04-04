namespace CrestCreates.Domain.Entities.Auditing
{
    public interface IMustHaveTenant
    {
        string TenantId { get; set; }
    }
}
