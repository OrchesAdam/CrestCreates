using System;
using System.Collections.Generic;
using CrestCreates.DistributedTransaction.CAP.Extensions;
using DotNetCore.CAP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace CrestCreates.DistributedTransaction.Tests;

/// <summary>
/// #125 R1-CAP-04：RabbitMQ URI 语义——amqp/amqps、显式/默认端口、TLS（ServerName）、编码 vhost 解码；
/// 纯主机名保留 CAP 原生 HostName 语义。
/// </summary>
public class CapTransportConfigurationTests
{
    [Fact]
    public void Amqp_uri_maps_host_port_credentials_and_plain_transport()
    {
        var options = ResolveRabbitOptions("amqp://alice:secret@rabbit-host:1234");

        Assert.Equal("rabbit-host", options.HostName);
        Assert.Equal(1234, options.Port);
        Assert.Equal("alice", options.UserName);
        Assert.Equal("secret", options.Password);
        Assert.Null(options.ConnectionFactoryOptions);
    }

    [Fact]
    public void Amqp_uri_without_port_defaults_to_5672()
    {
        var options = ResolveRabbitOptions("amqp://rabbit-host");

        Assert.Equal("rabbit-host", options.HostName);
        Assert.Equal(5672, options.Port);
    }

    [Fact]
    public void Amqps_uri_defaults_to_5671_and_enables_tls_with_server_name()
    {
        var options = ResolveRabbitOptions("amqps://secure-host");

        Assert.Equal("secure-host", options.HostName);
        Assert.Equal(5671, options.Port);

        Assert.NotNull(options.ConnectionFactoryOptions);
        var factory = new ConnectionFactory();
        options.ConnectionFactoryOptions!(factory);
        Assert.True(factory.Ssl.Enabled, "amqps must enable TLS on the connection factory");
        Assert.Equal("secure-host", factory.Ssl.ServerName);
    }

    [Fact]
    public void Encoded_root_vhost_decodes_to_slash()
    {
        var options = ResolveRabbitOptions("amqp://rabbit-host/%2F");

        Assert.Equal("/", options.VirtualHost);
    }

    [Fact]
    public void Custom_vhost_is_decoded()
    {
        var options = ResolveRabbitOptions("amqp://rabbit-host/my%20vhost");

        Assert.Equal("my vhost", options.VirtualHost);
    }

    [Fact]
    public void Plain_host_name_keeps_native_cap_semantics()
    {
        var options = ResolveRabbitOptions("rabbit-host");

        Assert.Equal("rabbit-host", options.HostName);
        Assert.Equal(-1, options.Port);
        Assert.Null(options.ConnectionFactoryOptions);
    }

    private static RabbitMQOptions ResolveRabbitOptions(string transportConnectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DistributedTransaction:CAP:StorageProvider"] = "PostgreSql",
                ["DistributedTransaction:CAP:TransportProvider"] = "RabbitMQ",
                ["DistributedTransaction:CAP:StorageConnectionString"] = "Host=localhost;Database=capdb;Username=u;Password=p",
                ["DistributedTransaction:CAP:TransportConnectionString"] = transportConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestCapDistributedTransaction(configuration);
        services.Configure<CrestCreates.DistributedTransaction.CAP.Options.DistributedTransactionCapOptions>(
            options => options.EnableCompensationBackgroundWorker = false);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<RabbitMQOptions>>().Value;
    }
}
