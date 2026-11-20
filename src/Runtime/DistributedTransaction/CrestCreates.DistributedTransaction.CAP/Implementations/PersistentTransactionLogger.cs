using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;
using CrestCreates.Data.Abstractions;

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

            var details = new TransactionErrorDetails(exception.Message, exception.StackTrace, exception.GetType().FullName);
            log.ErrorDetails = JsonSerializer.Serialize(details, TransactionErrorJsonContext.Default.TransactionErrorDetails);
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

    internal sealed record TransactionErrorDetails(string Message, string? StackTrace, string? Type);

    [JsonSerializable(typeof(TransactionErrorDetails))]
    internal partial class TransactionErrorJsonContext : JsonSerializerContext;
}
