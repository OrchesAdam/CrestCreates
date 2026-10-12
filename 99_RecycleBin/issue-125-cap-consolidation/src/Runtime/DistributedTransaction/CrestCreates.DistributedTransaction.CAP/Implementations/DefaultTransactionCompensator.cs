using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class DefaultTransactionCompensator : ITransactionCompensator
    {
        private readonly ConcurrentDictionary<Guid, TransactionCompensation> _compensations = new();

        public async Task CompensateAsync(Guid transactionId)
        {
            // 实现事务补偿逻辑
            // 这里可以根据事务ID查找需要补偿的操作并执行
            var pendingCompensations = await GetPendingCompensationsAsync(transactionId);
            foreach (var compensation in pendingCompensations)
            {
                try
                {
                    // 执行补偿逻辑
                    await MarkCompensationCompletedAsync(compensation.Id);
                }
                catch (Exception)
                {
                    await MarkCompensationFailedAsync(compensation.Id, "Compensation failed");
                }
            }
        }

        public async Task<bool> CanCompensateAsync(Guid transactionId)
        {
            // 判断是否可以补偿指定的事务
            return true;
        }

        public async Task RegisterCompensationAsync(
            Guid transactionId,
            string participantName,
            object? compensationData)
        {
            var compensation = new TransactionCompensation
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                ParticipantName = participantName,
                CompensationData = compensationData != null ? JsonSerializer.Serialize(compensationData) : null,
                Status = CompensationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _compensations[compensation.Id] = compensation;
        }

        public async Task<IEnumerable<TransactionCompensation>> GetPendingCompensationsAsync(Guid transactionId)
        {
            return _compensations.Values
                .Where(c => c.TransactionId == transactionId && c.Status == CompensationStatus.Pending);
        }

        public async Task MarkCompensationCompletedAsync(Guid compensationId)
        {
            if (_compensations.TryGetValue(compensationId, out var compensation))
            {
                compensation.Status = CompensationStatus.Completed;
                compensation.ExecutedAt = DateTime.UtcNow;
            }
        }

        public async Task MarkCompensationFailedAsync(Guid compensationId, string errorMessage)
        {
            if (_compensations.TryGetValue(compensationId, out var compensation))
            {
                compensation.Status = CompensationStatus.Failed;
                compensation.ErrorMessage = errorMessage;
                compensation.RetryCount++;
            }
        }

        public async Task ProcessRetryingCompensationsAsync()
        {
            var retryingCompensations = _compensations.Values
                .Where(c => c.Status == CompensationStatus.Retrying);

            foreach (var compensation in retryingCompensations)
            {
                try
                {
                    // 执行补偿逻辑
                    await MarkCompensationCompletedAsync(compensation.Id);
                }
                catch (Exception)
                {
                    await MarkCompensationFailedAsync(compensation.Id, "Retry compensation failed");
                }
            }
        }
    }
}
