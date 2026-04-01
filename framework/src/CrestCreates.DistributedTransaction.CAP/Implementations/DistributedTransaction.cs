using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Extensions;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class DistributedTransaction : IDistributedTransaction, ITransactionParticipantRegistry
    {
        private readonly List<ITransactionParticipant> _participants = new();
        private readonly IServiceProvider _serviceProvider;

        public DistributedTransaction(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            TransactionId = Guid.NewGuid();
            Status = TransactionStatus.Pending;
        }

        public Guid TransactionId { get; }
        public TransactionStatus Status { get; private set; }

        public async Task BeginAsync()
        {
            Status = TransactionStatus.Started;
            // 可以在这里添加事务开始的逻辑，如记录事务日志等
        }

        public async Task CommitAsync()
        {
            try
            {
                // 两阶段提交：第一阶段 - 准备
                var prepareTasks = _participants.Select(p => p.PrepareAsync());
                var prepareResults = await Task.WhenAll(prepareTasks);

                // 检查所有参与者是否准备成功
                if (prepareResults.All(result => result))
                {
                    // 第二阶段 - 提交
                    var commitTasks = _participants.Select(p => p.CommitAsync());
                    await Task.WhenAll(commitTasks);
                    Status = TransactionStatus.Committed;
                }
                else
                {
                    // 准备失败，回滚
                    await RollbackAsync();
                    throw new Exception("Transaction preparation failed");
                }
            }
            catch (Exception ex)
            {
                Status = TransactionStatus.Failed;
                throw;
            }
        }

        public async Task RollbackAsync()
        {
            try
            {
                var rollbackTasks = _participants.Select(p => p.RollbackAsync());
                await Task.WhenAll(rollbackTasks);
                Status = TransactionStatus.RolledBack;
            }
            catch (Exception ex)
            {
                Status = TransactionStatus.Failed;
                throw;
            }
        }

        public async Task CompensateAsync()
        {
            try
            {
                Status = TransactionStatus.Compensating;
                var compensateTasks = _participants.Select(p => p.CompensateAsync());
                await Task.WhenAll(compensateTasks);
                Status = TransactionStatus.Compensated;
            }
            catch (Exception ex)
            {
                Status = TransactionStatus.Failed;
                throw;
            }
        }

        public void AddParticipant(ITransactionParticipant participant)
        {
            _participants.Add(participant);
        }

        public void Dispose()
        {
            // 清理资源
        }
    }
}
