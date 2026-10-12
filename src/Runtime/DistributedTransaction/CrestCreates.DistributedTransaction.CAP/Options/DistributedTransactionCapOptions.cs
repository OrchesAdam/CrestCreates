namespace CrestCreates.DistributedTransaction.CAP.Options;

public class DistributedTransactionCapOptions
{
    public const string SectionName = "DistributedTransaction:CAP";

    public CapStorageProvider StorageProvider { get; set; } = CapStorageProvider.SqlServer;

    public string StorageConnectionString { get; set; } = string.Empty;

    public CapTransportProvider TransportProvider { get; set; } = CapTransportProvider.RabbitMQ;

    /// <summary>
    /// 传输连接配置。RabbitMQ：<c>amqp(s)://user:pass@host:port[/vhost]</c> URI（推荐）或纯主机名
    /// （CAP <c>UseRabbitMQ(string)</c> 的原生语义是 HostName）；Kafka：bootstrap servers。
    /// 无默认值：未配置即注册期失败（fail closed）。
    /// </summary>
    public string TransportConnectionString { get; set; } = string.Empty;

    public string DefaultGroup { get; set; } = "crestcreates";

    public int FailedRetryCount { get; set; } = 5;

    public int FailedRetryIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// CAP 扫描恢复的判定窗口（秒，透传 CapOptions.FallbackWindowLookbackSeconds）：
    /// 扫描模式下事务消息的首次投递依赖该窗口，受支持范围 [30, ∞)。
    /// </summary>
    public int FallbackWindowLookbackSeconds { get; set; } = 240;

    /// <summary>声明必需的补偿执行器名称（启动期校验覆盖；默认空表示仅校验 registry 可解析）。</summary>
    public string[] RequiredCompensationExecutors { get; set; } = System.Array.Empty<string>();

    public bool UseDashboard { get; set; }

    // Compensation retry options
    public int CompensationMaxRetries { get; set; } = 3;

    public int CompensationRetryIntervalSeconds { get; set; } = 30;

    public bool EnableCompensationBackgroundWorker { get; set; } = true;
}
