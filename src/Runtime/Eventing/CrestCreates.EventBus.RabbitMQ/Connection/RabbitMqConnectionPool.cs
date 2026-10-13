using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Exceptions;

namespace CrestCreates.EventBus.RabbitMQ.Connection;

/// <summary>
/// Manages the single long-lived RabbitMQ connection, a pool of publisher channels and the dedicated
/// consumer channel. MaxChannels bounds the publisher pool only; the dedicated consumer channel is
/// independent. Channel leases are released exactly once on every path (return, closed-channel
/// discard, disposal, acquisition failure).
/// </summary>
public sealed class RabbitMqConnectionPool : IAsyncDisposable, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionPool> _logger;
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    private readonly ConcurrentQueue<IChannel> _channelPool = new();
    private readonly SemaphoreSlim _channelSemaphore;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;
    private int _activeChannels;

    public RabbitMqConnectionPool(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionPool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _options.Validate();

        _channelSemaphore = new SemaphoreSlim(_options.MaxChannels, _options.MaxChannels);

        _factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            ConsumerDispatchConcurrency = (ushort)Math.Clamp(_options.ConsumerDispatchConcurrency, 1, ushort.MaxValue)
        };
    }

    /// <summary>
    /// Acquires a publisher channel lease from the bounded pool. Every successful acquisition must be
    /// released exactly once through <see cref="ReturnChannel"/>.
    /// </summary>
    public async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _channelSemaphore.WaitAsync(cancellationToken);
        var releaseOnFailure = true;
        try
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                await EnsureConnectionAsync(cancellationToken);

                if (_channelPool.TryDequeue(out var channel))
                {
                    if (channel.IsOpen)
                    {
                        releaseOnFailure = false;
                        return channel;
                    }

                    Interlocked.Decrement(ref _activeChannels);
                    SafeDispose(channel);
                }

                var created = await CreatePublisherChannelAsync();
                releaseOnFailure = false;
                return created;
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        finally
        {
            if (releaseOnFailure)
            {
                ReleaseSemaphoreQuietly();
            }
        }
    }

    /// <summary>
    /// Returns a publisher channel lease. Closed channels are discarded; the semaphore permit is
    /// released exactly once on every path. Never dispose a pooled channel directly.
    /// </summary>
    public void ReturnChannel(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (_disposed || !channel.IsOpen)
        {
            if (!_disposed)
            {
                Interlocked.Decrement(ref _activeChannels);
            }

            SafeDispose(channel);
            ReleaseSemaphoreQuietly();
            return;
        }

        _channelPool.Enqueue(channel);
        ReleaseSemaphoreQuietly();
    }

    /// <summary>
    /// Creates the dedicated, long-lived consumer channel. It is not counted against MaxChannels and
    /// must be closed by its owner (the consumer) during shutdown.
    /// </summary>
    public async Task<IChannel> CreateDedicatedChannelAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectionAsync(cancellationToken);
            Debug.Assert(_connection != null, "Connection should be established");
            var channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false),
                cancellationToken);
            _logger.LogDebug("Created dedicated consumer channel");
            return channel;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection != null && _connection.IsOpen)
        {
            return;
        }

        try
        {
            if (_connection is not null)
            {
                _connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
                _connection.CallbackExceptionAsync -= OnCallbackExceptionAsync;
                _connection.Dispose();
            }

            _connection = await _factory.CreateConnectionAsync(cancellationToken);

            _logger.LogInformation(
                "RabbitMQ connection established to {HostName}:{Port}",
                _options.HostName, _options.Port);

            _connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
            _connection.CallbackExceptionAsync += OnCallbackExceptionAsync;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish RabbitMQ connection to {HostName}:{Port}",
                _options.HostName, _options.Port);
            throw new RabbitMqConnectionException(
                $"Failed to connect to RabbitMQ at {_options.HostName}:{_options.Port}",
                _options.HostName, ex);
        }
    }

    private async Task<IChannel> CreatePublisherChannelAsync()
    {
        Debug.Assert(_connection != null, "Connection should be established");

        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true
        );

        var channel = await _connection.CreateChannelAsync(options);
        Interlocked.Increment(ref _activeChannels);

        _logger.LogDebug("Created new publisher channel, active channels: {Count}", _activeChannels);

        return channel;
    }

    private Task OnConnectionShutdownAsync(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning(
            "RabbitMQ connection shutdown: {ReplyCode} - {ReplyText}",
            e.ReplyCode, e.ReplyText);
        return Task.CompletedTask;
    }

    private Task OnCallbackExceptionAsync(object? sender, CallbackExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "RabbitMQ callback exception: {Detail}", e.Detail);
        return Task.CompletedTask;
    }

    private void ReleaseSemaphoreQuietly()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _channelSemaphore.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void SafeDispose(IChannel channel)
    {
        try
        {
            channel.Dispose();
        }
        catch (Exception)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        while (_channelPool.TryDequeue(out var channel))
        {
            Interlocked.Decrement(ref _activeChannels);
            SafeDispose(channel);
        }

        if (_connection is not null)
        {
            _connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
            _connection.CallbackExceptionAsync -= OnCallbackExceptionAsync;
            await _connection.DisposeAsync();
        }

        _channelSemaphore.Dispose();
        _connectionLock.Dispose();

        _logger.LogInformation("RabbitMQ connection pool disposed");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        while (_channelPool.TryDequeue(out var channel))
        {
            Interlocked.Decrement(ref _activeChannels);
            SafeDispose(channel);
        }

        if (_connection is not null)
        {
            _connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
            _connection.CallbackExceptionAsync -= OnCallbackExceptionAsync;
            _connection.Dispose();
        }

        _channelSemaphore.Dispose();
        _connectionLock.Dispose();

        _logger.LogInformation("RabbitMQ connection pool disposed");
    }
}
