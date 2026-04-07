namespace CrestCreates.DistributedTransaction.CAP.Options;

public class DistributedTransactionCapOptions
{
    public const string SectionName = "DistributedTransaction:CAP";

    public CapStorageProvider StorageProvider { get; set; } = CapStorageProvider.SqlServer;

    public string StorageConnectionString { get; set; } = string.Empty;

    public CapTransportProvider TransportProvider { get; set; } = CapTransportProvider.RabbitMQ;

    public string TransportConnectionString { get; set; } = "localhost";

    public string DefaultGroup { get; set; } = "crestcreates";

    public int FailedRetryCount { get; set; } = 5;

    public int FailedRetryIntervalSeconds { get; set; } = 60;

    public bool UseDashboard { get; set; }
}
