using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.CAP.Options;
using CrestCreates.DistributedTransaction.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestCreates.DistributedTransaction.CAP.BackgroundServices;

/// <summary>
/// 启动自检：在任何发布发生前冻结装配前提——触发最终 CapOptions 校验（第 2 层权威），
/// 并校验补偿执行器 registry/声明必需集合与持久化仓库可解析。只做解析与声明覆盖，不连库。
/// </summary>
public sealed class CapPublishReadinessValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public CapPublishReadinessValidator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 触发对「最终 CapOptions（所有 Configure/PostConfigure 之后）」的校验：
        // 解析即运行所有 IValidateOptions<CapOptions>（含 AddCrestCapDistributedTransaction 注册的扫描前提）。
        _ = _serviceProvider.GetRequiredService<IOptions<DotNetCore.CAP.CapOptions>>().Value;

        using var scope = _serviceProvider.CreateScope();
        var scoped = scope.ServiceProvider;

        var registry = scoped.GetRequiredService<ICompensationExecutorRegistry>();
        var options = scoped.GetRequiredService<IOptions<DistributedTransactionCapOptions>>().Value;

        var missing = options.RequiredCompensationExecutors
            .Where(name => registry.GetExecutor(name) is null)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "Required compensation executors are not registered: " + string.Join(", ", missing) +
                ". Register them ([CompensationExecutor] classes + the generated AddCompensationExecutors()) or fix " +
                "DistributedTransaction:CAP:RequiredCompensationExecutors.");
        }

        try
        {
            _ = scoped.GetRequiredService<IRepository<TransactionLog, Guid>>();
            _ = scoped.GetRequiredService<IRepository<TransactionCompensation, Guid>>();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "The CAP persistence repositories are not registered " +
                "(IRepository<TransactionLog>/IRepository<TransactionCompensation>). " +
                "Call AddCrestCapEfCoreIntegration(...) from the integration package before starting the host.",
                exception);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
