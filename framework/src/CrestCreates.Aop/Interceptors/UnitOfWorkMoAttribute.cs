using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Aop.Extensions;
using CrestCreates.OrmProviders.Abstract;
using Microsoft.Extensions.Logging;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.Aop.Interceptors;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class UnitOfWorkMoAttribute : AsyncMoAttribute
{
    private static readonly AsyncLocal<Stack<IUnitOfWorkScope?>?> CurrentScopes = new();
    private readonly bool _requiresTransaction;
    public int Order => InterceptorOrders.UnitOfWork;
    public bool RequiresTransaction => _requiresTransaction;

    public UnitOfWorkMoAttribute(bool requiresTransaction = true)
    {
        _requiresTransaction = requiresTransaction;
    }

    public override async ValueTask OnEntryAsync(MethodContext context)
    {
        IUnitOfWorkScope? scope = null;
        GetScopeStack().Push(null);

        try
        {
            var uowManager = context.GetService<IUnitOfWorkManager>();
            if (uowManager == null)
            {
                var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
                logger?.LogWarning("IUnitOfWorkManager 未注册，跳过工作单元");
                return;
            }

            scope = uowManager.BeginScope(isTransactional: _requiresTransaction);
            ReplaceTopScope(scope);

            try
            {
                if (scope.IsOwner && scope.IsTransactional)
                {
                    await scope.UnitOfWork.BeginTransactionAsync();
                }
            }
            catch
            {
                PopScope()?.Dispose();
                throw;
            }
        }
        catch
        {
            if (scope == null)
            {
                PopScope();
            }

            throw;
        }
    }

    public override async ValueTask OnSuccessAsync(MethodContext context)
    {
        var scope = PopScope();
        if (scope != null)
        {
            try
            {
                if (scope.IsOwner && scope.IsTransactional)
                {
                    await scope.UnitOfWork.CommitTransactionAsync();
                }
                else if (scope.IsOwner)
                {
                    await scope.UnitOfWork.SaveChangesAsync();
                }
            }
            catch (Exception exception)
            {
                var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
                logger?.LogError(exception, "工作单元提交失败");
                throw;
            }
            finally
            {
                scope.Dispose();
            }
        }
    }

    public override async ValueTask OnExceptionAsync(MethodContext context)
    {
        var scope = PopScope();
        if (scope != null)
        {
            try
            {
                if (scope.IsOwner)
                {
                    await scope.UnitOfWork.RollbackTransactionAsync();
                }
            }
            catch (Exception exception)
            {
                var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
                logger?.LogError(exception, "工作单元回滚失败");
            }
            finally
            {
                scope.Dispose();
            }
        }
    }

    private static Stack<IUnitOfWorkScope?> GetScopeStack()
    {
        return CurrentScopes.Value ??= new Stack<IUnitOfWorkScope?>();
    }

    private static void ReplaceTopScope(IUnitOfWorkScope? scope)
    {
        var scopes = GetScopeStack();
        if (scopes.Count == 0)
        {
            throw new InvalidOperationException("工作单元拦截器作用域栈状态异常");
        }

        scopes.Pop();
        scopes.Push(scope);
    }

    private static IUnitOfWorkScope? PopScope()
    {
        var scopes = CurrentScopes.Value;
        if (scopes == null || scopes.Count == 0)
        {
            return null;
        }

        var scope = scopes.Pop();
        if (scopes.Count == 0)
        {
            CurrentScopes.Value = null;
        }

        return scope;
    }
}
