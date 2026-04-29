using System;

namespace CrestCreates.Domain.Shared.Entities.Auditing
{
    public interface IMayHaveOrganization
    {
        Guid? OrganizationId { get; set; }
    }
}
