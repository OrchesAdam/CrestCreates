namespace CrestCreates.Domain.Entities.Auditing
{
    public interface IMustHaveTenant
    {
        string TenantId { get; set; }
    }

    public interface IMustHaveTenantOrganization : IMustHaveTenant, IMayHaveOrganization, IHasCreator, Domain.Shared.Entities.Auditing.IAuditedEntity
    {
    }
}
