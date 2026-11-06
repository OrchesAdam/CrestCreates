using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.Domain.Shared.Entities.Auditing;
using SqlSugar;

namespace CrestCreates.Data.SqlSugar.Repositories
{
    /// <summary>
    /// Provider-internal soft-delete action: one UTC/user snapshot per operation, one conditional
    /// UPDATE, no reliance on DataExecuting delete rewriting.
    /// </summary>
    internal static class SqlSugarSoftDeleteOperation
    {
        public static async Task<int> ExecuteAsync<TEntity>(
            IUpdateable<TEntity> update,
            ICurrentUserProvider? currentUserProvider,
            bool advanceConcurrencyStamp,
            CancellationToken cancellationToken)
            where TEntity : class, new()
        {
            var now = DateTime.UtcNow;
            var userId = currentUserProvider?.GetCurrentUserId();

            update = update
                .SetColumns(nameof(ISoftDelete.IsDeleted), true)
                .SetColumns(nameof(ISoftDelete.DeletionTime), now);

            if (userId.HasValue)
            {
                update = update.SetColumns(nameof(ISoftDelete.DeleterId), userId.Value);
            }

            if (advanceConcurrencyStamp)
            {
                update = update.SetColumns(
                    nameof(IHasConcurrencyStamp.ConcurrencyStamp),
                    Guid.NewGuid().ToString());
            }

            return await update.ExecuteCommandAsync(cancellationToken);
        }
    }
}
