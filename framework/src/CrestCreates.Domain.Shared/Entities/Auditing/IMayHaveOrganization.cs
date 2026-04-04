using System;

namespace CrestCreates.Domain.Entities.Auditing
{
    public interface IMayHaveOrganization
    {
        Guid? OrganizationId { get; set; }
    }
}
