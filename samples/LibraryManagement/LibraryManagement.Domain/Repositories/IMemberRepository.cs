using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Domain.Repositories;

public interface IMemberRepository : ICrestRepositoryBase<Member, Guid>
{
    Task<Member?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Member>> GetActiveMembersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Member>> GetMembersWithOverdueBalanceAsync(CancellationToken cancellationToken = default);
    Task<bool> IsEmailUniqueAsync(string email, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
