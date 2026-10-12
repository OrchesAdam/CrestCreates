using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Aop.Extensions;
using CrestCreates.Data.Abstractions;
using Microsoft.Extensions.Logging;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.Aop.Interceptors;

/// <summary>
/// 工作单元拦截器：把被拦截方法委托给唯一执行内核。
/// </summary>
/// <remarks>
/// 激活协议（见设计 §4.1）：回调实现**不得使用 async 关键字**——OnEntryAsync 的同步前缀在织入帧内
/// 完成 <see cref="IUnitOfWorkManager.BeginScope"/> 激活（async 方法体内的 AsyncLocal 写入不会传播到
/// 织入帧的方法体）；OnExitAsync 在织入帧内同步恢复（Pop + Dispose）。事务开始/完成/回滚/通知全部由
/// 内核按固定顺序执行，本拦截器不再自实现 begin/commit/flush/rollback。
/// </remarks>
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

    public override ValueTask OnEntryAsync(MethodContext context)
    {
        // 同步前缀（织入帧内执行，激活对方法体可见）。
        GetScopeStack().Push(null);

        try
        {
            var uowManager = context.GetService<IUnitOfWorkManager>();
            if (uowManager == null)
            {
                var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
                logger?.LogWarning("IUnitOfWorkManager 未注册，跳过工作单元");
                return default;
            }

            var options = new UnitOfWorkOptions { IsTransactional = _requiresTransaction };
            var scope = uowManager.BeginScope(options);
            ReplaceTopScope(scope);
            return new ValueTask(StartScopeAsync(scope, ResolveCallerToken(context)));
        }
        catch
        {
            PopScope()?.Dispose();
            throw;
        }
    }

    public override ValueTask OnSuccessAsync(MethodContext context)
    {
        var scope = PeekScope();
        if (scope is null)
        {
            return default;
        }

        return new ValueTask(CompleteScopeAsync(scope));
    }

    public override ValueTask OnExceptionAsync(MethodContext context)
    {
        var scope = PeekScope();
        if (scope is null)
        {
            return default;
        }

        return new ValueTask(RollbackScopeAsync(scope, context));
    }

    public override ValueTask OnExitAsync(MethodContext context)
    {
        // 同步恢复段（织入帧内可见）：环境恢复与资源释放恰好一次。
        try
        {
            PopScope()?.Dispose();
        }
        catch (Exception exception)
        {
            var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
            logger?.LogError(exception, "工作单元环境恢复失败");
        }

        return default;
    }

    private static async Task StartScopeAsync(IUnitOfWorkScope scope, CancellationToken callerToken)
    {
        await scope.StartAsync(callerToken).ConfigureAwait(false);
    }

    private static async Task CompleteScopeAsync(IUnitOfWorkScope scope)
    {
        await scope.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task RollbackScopeAsync(IUnitOfWorkScope scope, MethodContext context)
    {
        try
        {
            await scope.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // 回滚失败不得替换原始业务异常；结果可由 scope 状态检查。
            var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
            logger?.LogError(exception, "工作单元回滚失败");
        }
    }

    private static CancellationToken ResolveCallerToken(MethodContext context)
    {
        if (context.Arguments is { } arguments)
        {
            foreach (var argument in arguments)
            {
                if (argument is CancellationToken token)
                {
                    return token;
                }
            }
        }

        return default;
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

    private static IUnitOfWorkScope? PeekScope()
    {
        var scopes = CurrentScopes.Value;
        return scopes == null || scopes.Count == 0 ? null : scopes.Peek();
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
