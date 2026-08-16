using CrestCreates.Agent.Memory.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Explicit opt-in Agent Memory persistence surface. Replaces the four
/// development Store contracts with PostgreSQL implementations; the selected
/// <see cref="PostgreSqlAgentMemoryStore"/> implements conditional curation and
/// capabilities itself, discovered by casting — no separate descriptors.
/// </summary>
public static class PostgreSqlAgentMemoryPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesPostgreSqlAgentMemoryPersistence(
        this IServiceCollection services)
    {
        services.RemoveAll<IAgentConversationStore>();
        services.RemoveAll<IAgentTaskHistoryStore>();
        services.RemoveAll<IAgentCompressedContextStore>();
        services.RemoveAll<IAgentMemoryStore>();

        services.AddSingleton<PostgreSqlAgentMemoryLockManager>();
        services.AddSingleton<IAgentConversationStore, PostgreSqlAgentConversationStore>();
        services.AddSingleton<IAgentTaskHistoryStore, PostgreSqlAgentTaskHistoryStore>();
        services.AddSingleton<IAgentCompressedContextStore, PostgreSqlAgentCompressedContextStore>();
        services.AddSingleton<IAgentMemoryStore, PostgreSqlAgentMemoryStore>();

        return services;
    }
}
