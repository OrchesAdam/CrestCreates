// framework/src/CrestCreates.DistributedTransaction.CAP/Implementations/PersistentTransactionCompensator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;
using CrestCreates.DistributedTransaction.CAP.Options;
using CrestCreates.OrmProviders.Abstract.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class PersistentTransactionCompensator : ITransactionCompensator
    {
        private readonly IRepository<TransactionCompensation, Guid> _compensationRepo;
        private readonly ICompensationExecutorRegistry _executorRegistry;
        private readonly DistributedTransactionCapOptions _options;

        public PersistentTransactionCompensator(
            IRepository<TransactionCompensation, Guid> compensationRepo,
            ICompensationExecutorRegistry executorRegistry,
            IOptions<DistributedTransactionCapOptions> options)
        {
            _compensationRepo = compensationRepo;
            _executorRegistry = executorRegistry;
            _options = options.Value;
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
                CompensationData = compensationData != null
                    ? JsonSerializer.Serialize(compensationData)
                    : null,
                Status = CompensationStatus.Pending,
                MaxRetries = _options.CompensationMaxRetries,
                CreatedAt = DateTime.UtcNow
            };

            await _compensationRepo.AddAsync(compensation);
        }

        public async Task CompensateAsync(Guid transactionId)
        {
            var compensations = await GetPendingCompensationsAsync(transactionId);

            foreach (var compensation in compensations.OrderByDescending(x => x.CreatedAt))
            {
                await ExecuteCompensationAsync(compensation);
            }
        }

        public async Task<IEnumerable<TransactionCompensation>> GetPendingCompensationsAsync(Guid transactionId)
        {
            return await _compensationRepo.FindAsync(
                x => x.TransactionId == transactionId &&
                     (x.Status == CompensationStatus.Pending || x.Status == CompensationStatus.Retrying));
        }

        public async Task MarkCompensationCompletedAsync(Guid compensationId)
        {
            var compensation = await _compensationRepo.GetByIdAsync(compensationId);
            if (compensation != null)
            {
                compensation.Status = CompensationStatus.Completed;
                compensation.ExecutedAt = DateTime.UtcNow;
                await _compensationRepo.UpdateAsync(compensation);
            }
        }

        public async Task MarkCompensationFailedAsync(Guid compensationId, string errorMessage)
        {
            var compensation = await _compensationRepo.GetByIdAsync(compensationId);
            if (compensation != null)
            {
                compensation.Status = CompensationStatus.Failed;
                compensation.ErrorMessage = errorMessage;
                await _compensationRepo.UpdateAsync(compensation);
            }
        }

        public async Task ProcessRetryingCompensationsAsync()
        {
            var retryingCompensations = await _compensationRepo.FindAsync(
                x => x.Status == CompensationStatus.Retrying);

            foreach (var compensation in retryingCompensations)
            {
                var delay = CalculateRetryDelay(compensation.RetryCount);
                if (DateTime.UtcNow < compensation.UpdatedAt.Add(delay))
                    continue;

                await ExecuteCompensationAsync(compensation);
            }
        }

        public async Task<bool> CanCompensateAsync(Guid transactionId)
        {
            var compensations = await _compensationRepo.FindAsync(
                x => x.TransactionId == transactionId &&
                     x.Status != CompensationStatus.Failed);

            return compensations.Any();
        }

        private async Task ExecuteCompensationAsync(TransactionCompensation compensation)
        {
            var executor = _executorRegistry.GetExecutor(compensation.ParticipantName);
            if (executor == null)
            {
                await MarkCompensationFailedAsync(
                    compensation.Id,
                    $"No executor found for participant: {compensation.ParticipantName}");
                return;
            }

            compensation.Status = CompensationStatus.Executing;
            await _compensationRepo.UpdateAsync(compensation);

            try
            {
                await executor.ExecuteAsync(compensation.CompensationData);
                await MarkCompensationCompletedAsync(compensation.Id);
            }
            catch (Exception ex)
            {
                await HandleCompensationFailureAsync(compensation, ex);
            }
        }

        private async Task HandleCompensationFailureAsync(
            TransactionCompensation compensation,
            Exception ex)
        {
            compensation.RetryCount++;
            compensation.ErrorMessage = ex.Message;
            compensation.UpdatedAt = DateTime.UtcNow;

            if (compensation.RetryCount >= compensation.MaxRetries)
            {
                compensation.Status = CompensationStatus.Failed;
            }
            else
            {
                compensation.Status = CompensationStatus.Retrying;
            }

            await _compensationRepo.UpdateAsync(compensation);
        }

        private TimeSpan CalculateRetryDelay(int retryCount)
        {
            var baseInterval = _options.CompensationRetryIntervalSeconds;
            var delay = baseInterval * Math.Pow(2, retryCount - 1);
            return TimeSpan.FromSeconds(Math.Min(delay, 300));
        }
    }
}
