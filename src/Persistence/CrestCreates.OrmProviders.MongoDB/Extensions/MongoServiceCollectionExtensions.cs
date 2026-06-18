using System.Diagnostics.CodeAnalysis;
using CrestCreates.Domain.Shared.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.MongoDB.Repositories;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace CrestCreates.OrmProviders.MongoDB.Extensions;

/// <summary>
/// MongoDB 服务注册扩展
/// </summary>
public static class MongoServiceCollectionExtensions
{
    /// <summary>
    /// 添加 MongoDB 数据库
    /// </summary>
    public static IServiceCollection AddMongoDatabase(
        this IServiceCollection services,
        string connectionString,
        string databaseName)
    {
        services.AddSingleton<IMongoClient>(sp => new MongoClient(connectionString));
        services.AddScoped<IMongoDatabase>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            return client.GetDatabase(databaseName);
        });

        return services;
    }

    /// <summary>
    /// 注册 MongoDB 仓储
    /// </summary>
    [RequiresUnreferencedCode("ActivatorUtilities.CreateInstance requires constructor metadata which may be trimmed")]
    public static IServiceCollection AddMongoRepository<TEntity, TKey, TRepository>(
        this IServiceCollection services)
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
        where TRepository : MongoRepositoryBase<TEntity, TKey>
    {
        services.AddScoped<ICrestRepositoryBase<TEntity, TKey>>(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            var currentTenant = sp.GetService<ICurrentTenant>();
            return ActivatorUtilities.CreateInstance<TRepository>(sp, database, currentTenant);
        });

        return services;
    }

    /// <summary>
    /// 注册默认 MongoDB 仓储
    /// </summary>
    public static IServiceCollection AddDefaultMongoRepository<TEntity, TKey>(
        this IServiceCollection services)
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        services.AddScoped<ICrestRepositoryBase<TEntity, TKey>>(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            var currentTenant = sp.GetService<ICurrentTenant>();
            return new MongoRepositoryBase<TEntity, TKey>(database, currentTenant);
        });

        return services;
    }
}