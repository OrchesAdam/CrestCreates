using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Serialization;

namespace CrestCreates.EventBus.Kafka.Consuming;

/// <summary>
/// Background service that consumes messages from Kafka topics and dispatches them to handlers.
/// Uses manual settlement; forwarding and commit are not atomic and duplicates remain possible.
/// </summary>
public sealed class KafkaConsumer : BackgroundService
{
    private readonly KafkaProducerPool _producerPool;
    private readonly JsonSerializerContext _jsonContext;
    private readonly KafkaOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KafkaConsumer> _logger;
    private readonly List<KafkaSubscriptionInfo> _subscriptions;
    private readonly Func<ConsumerConfig, IConsumer<string, byte[]>> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaConsumer"/> class.
    /// </summary>
    /// <param name="producerPool">The producer pool for retry publishing.</param>
    /// <param name="jsonContext">The JSON serializer context for deserialization.</param>
    /// <param name="options">The Kafka options.</param>
    /// <param name="serviceProvider">The service provider for creating scopes.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
    public KafkaConsumer(
        KafkaProducerPool producerPool,
        JsonSerializerContext jsonContext,
        IOptions<KafkaOptions> options,
        IServiceProvider serviceProvider,
        ILogger<KafkaConsumer> logger) : this(producerPool, jsonContext, options, serviceProvider, logger, null) { }

    internal KafkaConsumer(KafkaProducerPool producerPool, JsonSerializerContext jsonContext,
        IOptions<KafkaOptions> options, IServiceProvider serviceProvider, ILogger<KafkaConsumer> logger,
        Func<ConsumerConfig, IConsumer<string, byte[]>>? factory)
    {
        _producerPool = producerPool ?? throw new ArgumentNullException(nameof(producerPool));
        _jsonContext = jsonContext ?? throw new ArgumentNullException(nameof(jsonContext));
        _options = options.Value;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _factory = factory ?? (config => new ConsumerBuilder<string, byte[]>(config).Build());
        _subscriptions = serviceProvider.GetServices<KafkaSubscriptionInfo>().ToList();
        if (_options.EnableAutoCommit)
            throw new ArgumentException("Kafka settlement requires EnableAutoCommit=false.", nameof(options));
        if (_options.RetryCount < 0 || _options.RetryDelaySeconds < 0 || string.IsNullOrWhiteSpace(_options.DeadLetterTopicSuffix))
            throw new ArgumentException("Kafka retry budget/delay must be nonnegative and DLQ suffix must be nonempty.", nameof(options));
        foreach (var group in _subscriptions.GroupBy(s => s.Topic, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1)
                throw new ArgumentException("Kafka requires exactly one explicit subscription per topic.");
            if (group.Single().ConsumerGroup is { } consumerGroup && consumerGroup != _options.ConsumerGroupId)
                throw new ArgumentException("Subscription ConsumerGroup must match KafkaOptions.ConsumerGroupId.");
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptions.Count == 0)
        {
            _logger.LogWarning("No Kafka subscriptions found. Consumer will not start.");
            return;
        }

        _logger.LogInformation("Starting Kafka consumer with {Count} subscriptions", _subscriptions.Count);

        var topics = _subscriptions.Select(s => s.Topic).Distinct().ToList();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            SessionTimeoutMs = _options.SessionTimeoutMs,
            MaxPollIntervalMs = _options.MaxPollIntervalMs,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = false
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

            consumerConfig.SecurityProtocol = securityProtocol;
            consumerConfig.SaslMechanism = saslMechanism;
            consumerConfig.SaslUsername = _options.SaslUsername;
            consumerConfig.SaslPassword = _options.SaslPassword;
        }

        using var consumer = _factory(consumerConfig);
        try
        {
            consumer.Subscribe(topics);
            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var record = consumer.Consume(stoppingToken);
                if (record != null) await ProcessMessageAsync(consumer, record, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Kafka consumption stopped with an unsettled record; restart required.");
            _serviceProvider.GetService<IHostApplicationLifetime>()?.StopApplication();
            throw;
        }
        finally
        {
            // Only the loop owns Close/Dispose. BackgroundService.StopAsync only cancels.
            consumer.Close();
        }
    }

    internal async Task ProcessMessageAsync(IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        KafkaMessageEnvelope? envelope = null;
        KafkaSubscriptionInfo? subscription = null;
        object? payload = null;
        string? reason = null;
        try
        {
            if (record.Message.Value == null) reason = "null-envelope";
            else
            {
                envelope = JsonSerializer.Deserialize(record.Message.Value, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope);
                if (envelope == null) reason = "null-envelope";
                else
                {
                    subscription = _subscriptions.SingleOrDefault(s => s.Topic == record.Topic);
                    if (subscription == null) reason = "unknown-subscription";
                    else if (!string.Equals(envelope.EventType, subscription.EventType.FullName ?? subscription.EventType.Name, StringComparison.Ordinal))
                        reason = "event-type-mismatch";
                    else if (_jsonContext.GetTypeInfo(subscription.EventType) is not { } typeInfo)
                        reason = "missing-type-info";
                    else if (envelope.Payload == null) reason = "invalid-payload";
                    else
                    {
                        payload = JsonSerializer.Deserialize(envelope.Payload, typeInfo);
                        if (payload == null) reason = "null-payload";
                        else if (envelope.RetryCount < 0) reason = "invalid-retry-count";
                    }
                }
            }
        }
        catch (JsonException) { reason = envelope == null ? "invalid-envelope" : "invalid-payload"; }
        catch (NotSupportedException) { reason = "unsupported-payload"; }

        if (reason != null)
            await ForwardAsync(record, record.Topic + _options.DeadLetterTopicSuffix,
                record.Message.Value, reason, envelope?.RetryCount ?? 0, cancellationToken);
        else
        {
            // Settlement is outside this catch: commit failure must not become a handler retry.
            Exception? failure = null;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await subscription!.InvokeHandler(scope.ServiceProvider, payload!, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { failure = ex; }
            if (failure != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogWarning(failure, "Kafka handler failed on {Topic}", record.Topic);
                if (envelope!.RetryCount < _options.RetryCount)
                {
                    if (_options.RetryDelaySeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), cancellationToken);
                    envelope.RetryCount++;
                    await ForwardAsync(record, record.Topic,
                        JsonSerializer.SerializeToUtf8Bytes(envelope, KafkaMessageEnvelopeContext.Default.KafkaMessageEnvelope),
                        "handler-retry", envelope.RetryCount, cancellationToken);
                }
                else
                    await ForwardAsync(record, record.Topic + _options.DeadLetterTopicSuffix,
                        record.Message.Value, "handler-retries-exhausted", envelope.RetryCount, cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        consumer.Commit(record);
    }

    private async Task ForwardAsync(ConsumeResult<string, byte[]> record, string topic,
        byte[]? value, string reason, int retryCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var headers = new Headers();
        if (record.Message.Headers != null)
            foreach (var header in record.Message.Headers) headers.Add(header.Key, header.GetValueBytes());
        headers.Add("x-settlement-reason", Encoding.UTF8.GetBytes(reason));
        headers.Add("x-retry-count", Encoding.UTF8.GetBytes(retryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        headers.Add("x-original-topic", Encoding.UTF8.GetBytes(record.Topic));
        headers.Add("x-original-partition", Encoding.UTF8.GetBytes(record.Partition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        headers.Add("x-original-offset", Encoding.UTF8.GetBytes(record.Offset.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var producer = await _producerPool.GetProducerAsync(cancellationToken);
        try
        {
            var delivery = await producer.ProduceAsync(topic, new Message<string, byte[]>
            {
                Key = record.Message.Key, Value = value!, Headers = headers,
                Timestamp = record.Message.Timestamp.Type == TimestampType.NotAvailable
                    ? Timestamp.Default
                    : new Timestamp(record.Message.Timestamp.UnixTimestampMs, TimestampType.CreateTime)
            }, cancellationToken);
            if (delivery.Status != PersistenceStatus.Persisted)
                throw new InvalidOperationException("Kafka forwarding delivery was not confirmed persisted.");
            _logger.LogWarning("Kafka record {Topic}/{Partition}/{Offset} forwarded to {Target}: {Reason}",
                record.Topic, record.Partition, record.Offset, topic, reason);
        }
        finally { _producerPool.ReturnProducer(producer); }
    }
}
