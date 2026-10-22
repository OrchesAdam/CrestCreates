using System;
using System.Runtime.CompilerServices;
using CrestCreates.Domain.Shared.Entities.Auditing;
using SqlSugar;

namespace CrestCreates.Data.SqlSugar.Repositories
{
    /// <summary>
    /// Registers the mapping-aware soft-delete query filter once per client. The filter expression
    /// uses the entity property, so renamed physical columns stay correct.
    /// </summary>
    internal static class SqlSugarSoftDeleteFilterRegistration
    {
        private static readonly ConditionalWeakTable<ISqlSugarClient, object> RegisteredClients = new();

        public static void EnsureRegistered(ISqlSugarClient client, Type entityType)
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType))
            {
                return;
            }

            if (RegisteredClients.TryAdd(client, new object()))
            {
                client.QueryFilter.Add(new TableFilterItem<ISoftDelete>(entity => entity.IsDeleted == false));
            }
        }
    }
}
