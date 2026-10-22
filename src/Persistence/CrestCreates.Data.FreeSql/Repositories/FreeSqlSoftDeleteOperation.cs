using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using FreeSql;

namespace CrestCreates.Data.FreeSql.Repositories
{
    /// <summary>
    /// Provider-internal soft-delete action: one UTC/user snapshot per operation, one conditional
    /// UPDATE, no reliance on CurdBefore/DataExecuting delete rewriting.
    /// </summary>
    internal static class FreeSqlSoftDeleteOperation
    {
        public static async Task<int> ExecuteAsync<TEntity>(
            IUpdate<TEntity> update,
            string liveRowColumn,
            ICurrentUserProvider? currentUserProvider,
            bool advanceConcurrencyStamp,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            var now = DateTime.UtcNow;
            var userId = currentUserProvider?.GetCurrentUserId();

            update = update
                .SetByPropertyName(nameof(ISoftDelete.IsDeleted), true)
                .SetByPropertyName(nameof(ISoftDelete.DeletionTime), now)
                .Where($"{liveRowColumn} = 0", new { });

            if (userId.HasValue)
            {
                update = update.SetByPropertyName(nameof(ISoftDelete.DeleterId), userId.Value);
            }

            if (advanceConcurrencyStamp)
            {
                update = update.SetByPropertyName(
                    nameof(IHasConcurrencyStamp.ConcurrencyStamp),
                    Guid.NewGuid().ToString("N"));
            }

            return await update.ExecuteAffrowsAsync(cancellationToken);
        }
    }
}
