using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CrestCreates.Infrastructure.Authorization
{
    public interface IOrganizationHierarchyService
    {
        Task<List<Guid>> GetOrganizationAndSubIdsAsync(Guid organizationId);
    }
}
