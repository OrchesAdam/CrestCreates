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
/// Manages a pool of RabbitMQ channels for concurrent message processing.
/// Uses RabbitMQ.Client 7.x async API where IModel is renamed to IChannel.
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

        if (_options.MaxChannels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RabbitMqOptions.MaxChannels),
                "MaxChannels must be greater than 0.");
        }

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
            RequestedHeartbeat = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _channelSemaphore.WaitAsync(cancellationToken);

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectionAsync(cancellationToken);

            if (_channelPool.TryDequeue(out var channel))
            {
                if (channel.IsOpen)
                {
                    return channel;
                }
                // Channel is closed, dispose and decrement counter
                Interlocked.Decrement(ref _activeChannels);
                channel.Dispose();
            }

            try
            {
                return await CreateChannelAsync();
            }
            catch
            {
                _channelSemaphore.Release();
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void ReturnChannel(IChannel channel)
    {
        if (_disposed || !channel.IsOpen)
        {
            Interlocked.Decrement(ref _activeChannels);
            channel.Dispose();
            _channelSemaphore.Release();
            return;
        }

        _channelPool.Enqueue(channel);
        _channelSemaphore.Release();
    }

    private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection != null && _connection.IsOpen)
        {
            return;
        }

        try
        {
            _connection?.Dispose();
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

    private async Task<IChannel> CreateChannelAsync()
    {
        Debug.Assert(_connection != null, "Connection should be established");

        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true
        );

        var channel = await _connection.CreateChannelAsync(options);
        Interlocked.Increment(ref _activeChannels);

        _logger.LogDebug("Created new channel, active channels: {Count}", _activeChannels);

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        while (_channelPool.TryDequeue(out var channel))
        {
            Interlocked.Decrement(ref _activeChannels);
            channel.Dispose();
        }

        if (_connection is not null)
        {
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
            channel.Dispose();
        }

        _connection?.Dispose();
        _channelSemaphore.Dispose();
        _connectionLock.Dispose();

        _logger.LogInformation("RabbitMQ connection pool disposed");
    }
}
