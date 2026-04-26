using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CrestCreates.DistributedTransaction.Abstractions;
using CrestCreates.DistributedTransaction.Models;

namespace CrestCreates.DistributedTransaction.CAP.Implementations
{
    public class TransactionLogger : ITransactionLogger
    {
        private readonly ConcurrentDictionary<Guid, TransactionStatus> _transactionStatuses = new();
        private readonly ConcurrentDictionary<Guid, TransactionLog> _transactionLogs = new();

        public async Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string? message = null)
        {
            _transactionStatuses[transactionId] = status;

            var log = _transactionLogs.GetOrAdd(transactionId, id => new TransactionLog
            {
                Id = id,
                CreatedAt = DateTime.UtcNow
            });
            log.Status = status;
            log.Message = message;
            log.UpdatedAt = DateTime.UtcNow;

            // 这里可以添加实际的日志记录逻辑，如写入数据库或日志文件
            Console.WriteLine($"Transaction {transactionId} status: {status} - {message}");
        }

        public async Task LogTransactionErrorAsync(Guid transactionId, Exception exception)
        {
            var log = _transactionLogs.GetOrAdd(transactionId, id => new TransactionLog
            {
                Id = id,
                CreatedAt = DateTime.UtcNow,
                Status = TransactionStatus.Failed
            });

            log.ErrorDetails = $"{{\"Message\":\"{exception.Message}\",\"StackTrace\":\"{exception.StackTrace}\",\"Type\":\"{exception.GetType().FullName}\"}}";
            log.UpdatedAt = DateTime.UtcNow;

            // 记录事务错误
            Console.WriteLine($"Transaction {transactionId} error: {exception.Message}");
        }

        public async Task<TransactionStatus?> GetTransactionStatusAsync(Guid transactionId)
        {
            if (_transactionStatuses.TryGetValue(transactionId, out var status))
            {
                return status;
            }
            return null;
        }

        public async Task<TransactionLog?> GetTransactionAsync(Guid transactionId)
        {
            if (_transactionLogs.TryGetValue(transactionId, out var log))
            {
                return log;
            }
            return null;
        }

        public async Task LogParticipantCountAsync(Guid transactionId, int count)
        {
            if (_transactionLogs.TryGetValue(transactionId, out var log))
            {
                log.ParticipantCount = count;
                log.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
