using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.DistributedTransaction.Tests;

/// <summary>
/// 供 Source Generator 识别的补偿执行器属性（名称匹配 CompensationExecutorAttribute）。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CompensationExecutorAttribute : Attribute
{
}

/// <summary>
/// 测试执行器：实现 ICompensationExecutor，由生成器注册进真实生成的 registry。
/// </summary>
[CompensationExecutor]
public sealed class TestChargeCompensationExecutor : ICompensationExecutor
{
    public const string ExecutorName = "test-charge";

    public string Name => ExecutorName;

    public List<string?> Invocations { get; } = new();

    public bool ThrowOnExecute { get; set; }

    public Task ExecuteAsync(string? compensationData)
    {
        Invocations.Add(compensationData);
        if (ThrowOnExecute)
        {
            throw new InvalidOperationException("executor-failure");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 单元级仓储替身（仅用于补偿/日志链的编排断言；真实 EF 仓储链由 PG 集成用例覆盖）。
/// 覆盖补偿链实际使用的方法，其余成员确定性拒绝。
/// </summary>
public sealed class TestCapRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    public List<TEntity> Items { get; } = new();

    public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (!Items.Contains(entity))
        {
            Items.Add(entity);
        }

        return Task.FromResult(entity);
    }

    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(entity => entity.Id!.Equals(id)));

    public Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.AsQueryable().FirstOrDefault(predicate));

    public Task<List<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.AsQueryable().Where(predicate).ToList());

    public Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Items.ToList());

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.AsQueryable().Any(predicate));

    public Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => Task.FromResult(predicate is null ? Items.Count : Items.AsQueryable().Count(predicate));

    public Task<long> LongCountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => Task.FromResult(predicate is null ? (long)Items.Count : Items.AsQueryable().LongCount(predicate));

    public Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Items.AddRange(entities);
        return Task.CompletedTask;
    }

    public Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var list = entities.ToList();
        foreach (var entity in list.Where(entity => !Items.Contains(entity)))
        {
            Items.Add(entity);
        }

        return Task.FromResult(list.Count);
    }

    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Items.Remove(entity);
        return Task.CompletedTask;
    }

    public Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var removed = 0;
        foreach (var entity in entities.ToList())
        {
            if (Items.Remove(entity))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task<int> DeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var matches = Items.AsQueryable().Where(predicate).ToList();
        foreach (var match in matches)
        {
            Items.Remove(match);
        }

        return Task.FromResult(matches.Count);
    }

    public IQueryableBuilder<TEntity> AsQueryable()
        => throw new NotSupportedException("Unit-level test repository does not support IQueryableBuilder.");

    public Task<List<TEntity>> GetByIdsAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return Task.FromResult(Items.Where(entity => idList.Contains(entity.Id)).ToList());
    }

    public Task DeleteByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var match = Items.FirstOrDefault(entity => entity.Id!.Equals(id));
        if (match is not null)
        {
            Items.Remove(match);
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteByIdsAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var matches = Items.Where(entity => idList.Contains(entity.Id)).ToList();
        foreach (var match in matches)
        {
            Items.Remove(match);
        }

        return Task.FromResult(matches.Count);
    }
}
