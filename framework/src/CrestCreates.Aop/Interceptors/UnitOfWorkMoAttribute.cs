using System;
using System.Threading.Tasks;
using CrestCreates.Aop.Abstractions;
using CrestCreates.Aop.Extensions;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.OrmProviders.Abstract;
using Microsoft.Extensions.Logging;
using Rougamo;
using Rougamo.Context;

namespace CrestCreates.Aop.Interceptors;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class UnitOfWorkMoAttribute : AsyncMoAttribute
{
    private readonly bool _requiresTransaction;
    private IUnitOfWork? _unitOfWork;
    public int Order => InterceptorOrders.UnitOfWork;

    public UnitOfWorkMoAttribute(bool requiresTransaction = true)
    {
        _requiresTransaction = requiresTransaction;
    }

    public override ValueTask OnEntryAsync(MethodContext context)
    {
        try
        {
            var uowManager = context.GetService<IUnitOfWorkManager>();
            if (uowManager == null)
            {
                var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
                logger?.LogWarning("IUnitOfWorkManager 未注册，跳过工作单元");
                return ValueTask.CompletedTask;
            }

            _unitOfWork = uowManager.Begin();
            return ValueTask.CompletedTask;
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }
    }

    public override async ValueTask OnSuccessAsync(MethodContext context)
    {
        if (_unitOfWork != null)
        {
            try
            {
                if (_requiresTransaction)
                {
                    await _unitOfWork.CommitTransactionAsync();
                }
                else
                {
                    await _unitOfWork.SaveChangesAsync();
                }
            }
            catch (Exception exception)
            {
                var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
                logger?.LogError(exception, "工作单元提交失败");
                throw;
            }
        }
    }

    public override async ValueTask OnExceptionAsync(MethodContext context)
    {
        if (_unitOfWork != null)
        {
            try
            {
                await _unitOfWork.RollbackTransactionAsync();
            }
            catch (Exception exception)
            {
                var logger = context.GetService<ILogger<UnitOfWorkMoAttribute>>();
                logger?.LogError(exception, "工作单元回滚失败");
            }
        }
    }
}
