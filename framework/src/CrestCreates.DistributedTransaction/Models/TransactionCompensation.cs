using System;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.DistributedTransaction.Models
{
    public class TransactionCompensation : IEntity<Guid>
    {
        public Guid Id { get; set; }
        public Guid TransactionId { get; set; }
        public string ParticipantName { get; set; } = string.Empty;
        public string? CompensationData { get; set; }
        public CompensationStatus Status { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
    }
}