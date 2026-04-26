using System;
using CrestCreates.Domain.Shared.Entities;

namespace CrestCreates.DistributedTransaction.Models
{
    public class TransactionLog : IEntity<Guid>
    {
        public Guid Id { get; set; }
        public TransactionStatus Status { get; set; }
        public string? Message { get; set; }
        public string? ErrorDetails { get; set; }
        public int ParticipantCount { get; set; }
        public Guid? CorrelationId { get; set; }
        public string? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}