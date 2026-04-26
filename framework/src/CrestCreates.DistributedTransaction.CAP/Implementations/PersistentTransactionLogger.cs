using System;
using System.Text.Json;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class PersistentTransactionLogger : ITransactionLogger
    {
        private readonly IRepository<TransactionLog, Guid> _repository;

        public PersistentTransactionLogger(IRepository<TransactionLog, Guid> repository)
        {
            _repository = repository;
        }

        public async Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string? message = null)
        {
            var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);

            if (log == null)
            {
                log = new TransactionLog
                {
                    Id = transactionId,
                    CreatedAt = DateTime.UtcNow
                };
                await _repository.AddAsync(log);
            }

            log.Status = status;
            log.Message = message;
            log.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(log);
        }

        public async Task LogTransactionErrorAsync(Guid transactionId, Exception exception)
        {
            var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
            if (log == null)
            {
                log = new TransactionLog
                {
                    Id = transactionId,
                    CreatedAt = DateTime.UtcNow,
                    Status = TransactionStatus.Failed
                };
                await _repository.AddAsync(log);
            }

            log.ErrorDetails = JsonSerializer.Serialize(new
            {
                exception.Message,
                exception.StackTrace,
                Type = exception.GetType().FullName
            });
            log.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(log);
        }

        public async Task<TransactionStatus?> GetTransactionStatusAsync(Guid transactionId)
        {
            var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
            return log?.Status;
        }

        public async Task<TransactionLog?> GetTransactionAsync(Guid transactionId)
        {
            return await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
        }

        public async Task LogParticipantCountAsync(Guid transactionId, int count)
        {
            var log = await _repository.FirstOrDefaultAsync(x => x.Id == transactionId);
            if (log != null)
            {
                log.ParticipantCount = count;
                log.UpdatedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(log);
            }
        }
    }
}
