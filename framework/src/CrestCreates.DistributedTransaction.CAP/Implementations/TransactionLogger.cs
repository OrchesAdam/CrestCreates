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

        public async Task LogTransactionAsync(Guid transactionId, TransactionStatus status, string message = null)
        {
            _transactionStatuses[transactionId] = status;
            // 这里可以添加实际的日志记录逻辑，如写入数据库或日志文件
            Console.WriteLine($"Transaction {transactionId} status: {status} - {message}");
        }

        public async Task LogTransactionErrorAsync(Guid transactionId, Exception exception)
        {
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
    }
}
