namespace CrestCreates.Runtime.Delivery.Abstractions.Contracts;

public static class OutboxContractLimits
{
    public const int MaxSemanticIdentifierLength = 256;
    public const int MaxFailureCodeLength = 128;
    public const int MaxRequiredConsumerCount = 32;
    public const int MinPayloadBytes = 1;
    public const int MaxPayloadBytes = 1_048_576;
    public const int MinBatchSize = 1;
    public const int MaxBatchSize = 256;
    public const int MinLeaseSeconds = 1;
    public const int MaxLeaseMinutes = 15;
    public const int MinHandlerTimeoutMilliseconds = 100;
    public const int MinRetryDelayMilliseconds = 10;
    public const int MaxRetryDelayMinutes = 60;
}
