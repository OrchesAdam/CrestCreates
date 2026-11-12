using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrestCreates.EventBus.Kafka.Options;

namespace CrestCreates.EventBus.Kafka.Connection;

/// <summary>
/// A thread-safe pool of Kafka producers that manages producer lifecycle and reuse.
/// Producers are created on-demand and returned to the pool for reuse, improving
/// performance by avoiding frequent producer creation and disposal.
/// </summary>
public sealed class KafkaProducerPool : IAsyncDisposable, IDisposable
{
    private readonly KafkaOptions _options;
    private readonly int _capacity;
    private readonly ILogger<KafkaProducerPool> _logger;
    private readonly object _gate = new();
    private readonly Queue<IProducer<string, byte[]>> _producerPool = new();
    private readonly HashSet<IProducer<string, byte[]>> _borrowed = new(ReferenceEqualityComparer.Instance);
    private readonly ProducerConfig _producerConfig;
    private readonly Func<IProducer<string, byte[]>> _factory;
    private TaskCompletionSource _changed = NewSignal();
    private bool _disposed;
    private int _leased;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalChanged()
    {
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }

    public KafkaProducerPool(
        IOptions<KafkaOptions> options,
        ILogger<KafkaProducerPool> logger) : this(options, logger, null)
    {
    }

    internal KafkaProducerPool(IOptions<KafkaOptions> options, ILogger<KafkaProducerPool> logger,
        Func<IProducer<string, byte[]>>? factory)
    {
        _factory = factory ?? CreateProducer;
        _options = options.Value;
        _logger = logger;
        _capacity = _options.ProducerPoolSize;

        if (_options.ProducerPoolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ProducerPoolSize must be greater than 0.");
        }

        _producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MaxInFlight = 5,
            // librdkafka idempotence requires transport retries even with zero handler retries.
            MessageSendMaxRetries = Math.Max(1, _options.RetryCount),
            RetryBackoffMs = (int)Math.Clamp((long)_options.RetryDelaySeconds * 1000, 1, 300000),
            LingerMs = 10,
            BatchSize = 16384,
            CompressionType = CompressionType.Snappy
        };

        if (!string.IsNullOrEmpty(_options.SaslUsername) && !string.IsNullOrEmpty(_options.SaslPassword))
        {
            if (!Enum.TryParse<SecurityProtocol>(_options.SecurityProtocol, out var securityProtocol))
            {
                throw new ArgumentException(
                    $"Invalid SecurityProtocol value: '{_options.SecurityProtocol}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<SecurityProtocol>())}");
            }

            if (!Enum.TryParse<SaslMechanism>(_options.SaslMechanism, out var saslMechanism))
            {
                throw new ArgumentException(
                    $"Invalid SaslMechanism value: '{_options.SaslMechanism}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<SaslMechanism>())}");
            }

            _producerConfig.SecurityProtocol = securityProtocol;
            _producerConfig.SaslMechanism = saslMechanism;
            _producerConfig.SaslUsername = _options.SaslUsername;
            _producerConfig.SaslPassword = _options.SaslPassword;
        }

        _logger.LogInformation(
            "Kafka producer pool initialized with {PoolSize} producers for {BootstrapServers}",
            _options.ProducerPoolSize, _options.BootstrapServers);
    }

    public async Task<IProducer<string, byte[]>> GetProducerAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (_leased < _capacity)
                {
                    _leased++;
                    if (_producerPool.TryDequeue(out var idle))
                    {
                        _borrowed.Add(idle);
                        return idle;
                    }
                    break;
                }
                wait = _changed.Task;
            }
            await wait.WaitAsync(cancellationToken);
        }

        IProducer<string, byte[]>? producer = null;
        try
        {
            producer = _factory();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                _borrowed.Add(producer);
                return producer;
            }
        }
        catch
        {
            lock (_gate)
            {
                _leased--;
                SignalChanged();
            }
            producer?.Dispose();
            throw;
        }
    }

    public void ReturnProducer(IProducer<string, byte[]> producer)
    {
        bool dispose;
        lock (_gate)
        {
            if (!_borrowed.Remove(producer))
                throw new InvalidOperationException("Producer is not currently borrowed from this pool.");
            _leased--;
            dispose = _disposed;
            if (!dispose) _producerPool.Enqueue(producer);
            SignalChanged();
        }
        if (dispose) producer.Dispose();
    }

    private IProducer<string, byte[]> CreateProducer()
    {
        var producer = new ProducerBuilder<string, byte[]>(_producerConfig)
            .SetErrorHandler((_, e) =>
            {
                _logger.LogError("Kafka producer error: {Reason} (code: {Code})", e.Reason, e.Code);
            })
            .SetStatisticsHandler((_, json) =>
            {
                _logger.LogDebug("Kafka producer statistics: {Stats}", json);
            })
            .Build();

        return producer;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        IProducer<string, byte[]>[] idle;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            idle = _producerPool.ToArray();
            _producerPool.Clear();
            SignalChanged();
        }
        // Outstanding leases keep ownership until ReturnProducer; waiters observe disposal.
        List<Exception>? errors = null;
        foreach (var producer in idle)
        {
            try { producer.Dispose(); }
            catch (Exception ex) { (errors ??= new()).Add(ex); }
        }
        if (errors != null) throw new AggregateException(errors);
    }
}
