using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrestCreates.EventBus.Kafka.Options;

namespace CrestCreates.EventBus.Kafka.Connection;

public sealed class KafkaProducerPool : IAsyncDisposable
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaProducerPool> _logger;
    private readonly ConcurrentQueue<IProducer<string, byte[]>> _producerPool = new();
    private readonly SemaphoreSlim _producerSemaphore;
    private readonly ProducerConfig _producerConfig;
    private bool _disposed;
    private int _activeProducers;

    public KafkaProducerPool(
        IOptions<KafkaOptions> options,
        ILogger<KafkaProducerPool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _producerSemaphore = new SemaphoreSlim(_options.ProducerPoolSize, _options.ProducerPoolSize);

        _producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MaxInFlight = 5,
            MessageSendMaxRetries = _options.RetryCount,
            RetryBackoffMs = _options.RetryDelaySeconds * 1000,
            LingerMs = 10,
            BatchSize = 16384,
            CompressionType = CompressionType.Snappy
        };

        if (!string.IsNullOrEmpty(_options.SaslUsername) && !string.IsNullOrEmpty(_options.SaslPassword))
        {
            _producerConfig.SecurityProtocol = Enum.Parse<SecurityProtocol>(_options.SecurityProtocol);
            _producerConfig.SaslMechanism = Enum.Parse<SaslMechanism>(_options.SaslMechanism);
            _producerConfig.SaslUsername = _options.SaslUsername;
            _producerConfig.SaslPassword = _options.SaslPassword;
        }

        _logger.LogInformation(
            "Kafka producer pool initialized with {PoolSize} producers for {BootstrapServers}",
            _options.ProducerPoolSize, _options.BootstrapServers);
    }

    public async Task<IProducer<string, byte[]>> GetProducerAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _producerSemaphore.WaitAsync(cancellationToken);

        if (_producerPool.TryDequeue(out var producer))
        {
            return producer;
        }

        return CreateProducer();
    }

    public void ReturnProducer(IProducer<string, byte[]> producer)
    {
        if (_disposed)
        {
            producer.Dispose();
            _producerSemaphore.Release();
            return;
        }

        _producerPool.Enqueue(producer);
        _producerSemaphore.Release();
    }

    private IProducer<string, byte[]> CreateProducer()
    {
        var producer = new ProducerBuilder<string, byte[]>(_producerConfig)
            .SetErrorHandler((p, e) =>
            {
                _logger.LogError("Kafka producer error: {Reason} (code: {Code})", e.Reason, e.Code);
            })
            .SetStatisticsHandler((p, json) =>
            {
                _logger.LogDebug("Kafka producer statistics: {Stats}", json);
            })
            .Build();

        Interlocked.Increment(ref _activeProducers);

        _logger.LogDebug("Created new Kafka producer, active producers: {Count}", _activeProducers);

        return producer;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        while (_producerPool.TryDequeue(out var producer))
        {
            producer.Dispose();
        }

        _producerSemaphore.Dispose();

        _logger.LogInformation("Kafka producer pool disposed");

        await ValueTask.CompletedTask;
    }
}
