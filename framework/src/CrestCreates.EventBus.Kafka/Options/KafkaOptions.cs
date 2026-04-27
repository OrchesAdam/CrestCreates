namespace CrestCreates.EventBus.Kafka.Options;

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
    public string SecurityProtocol { get; set; } = "Plaintext";
    public string SaslMechanism { get; set; } = "Plain";
    public int ProducerPoolSize { get; set; } = 4;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public string ConsumerGroupId { get; set; } = "crestcreates-consumers";
    public bool EnableAutoCommit { get; set; } = false;
    public int AutoCommitIntervalMs { get; set; } = 5000;
    public int SessionTimeoutMs { get; set; } = 30000;
    public int MaxPollIntervalMs { get; set; } = 300000;
    public string DeadLetterTopicSuffix { get; set; } = ".dlq";
    public string DefaultTopic { get; set; } = "crestcreates.events";
    public int PartitionsPerTopic { get; set; } = 3;
    public short ReplicationFactor { get; set; } = 1;
}
