using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;

namespace CrestCreates.TestServices;

public enum DedicatedTestService
{
    CapPostgres,
    CapRabbitMq,
    HealthPostgres,
    HealthRedis
}

/// <summary>故障注入只允许停启测试 Compose 中验证过的专属容器。</summary>
public static class TestServiceControl
{
    private const string ComposeFile = "docker-compose.test-services.yml";
    private const string ComposeProject = "crest-test-services";
    private const string Loopback = "127.0.0.1";
    private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(45);
    private static readonly Lazy<Task<string>> CliName = new(DetectCliAsync);

    public static Task<ServiceLease> AcquirePostgresAsync(DedicatedTestService service, string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        return AcquireAsync(service, connection.Host, connection.Port);
    }

    public static async Task<ServiceLease> AcquireAsync(DedicatedTestService service, string? host, int port)
    {
        var (serviceName, containerName, variable, publishedPort, targetPort) = service switch
        {
            DedicatedTestService.CapPostgres => ("cap-pg", "crest-test-cap-pg", "CREST_CAP_TEST_PG_CONTAINER", 5433, 5432),
            DedicatedTestService.CapRabbitMq => ("cap-rabbitmq", "crest-test-cap-rabbitmq", "CREST_CAP_TEST_RABBIT_CONTAINER", 5673, 5672),
            DedicatedTestService.HealthPostgres => ("health-pg", "crest-test-health-pg", "CREST_HEALTH_TEST_PG_CONTAINER", 5434, 5432),
            DedicatedTestService.HealthRedis => ("redis", "crest-test-health-redis", "CREST_HEALTH_TEST_REDIS_CONTAINER", 6379, 6379),
            _ => throw new ArgumentOutOfRangeException(nameof(service))
        };
        if (ExternalTestServices.RequiredContainerName(variable, serviceName) != containerName ||
            host != Loopback || port != publishedPort)
        {
            throw new InvalidOperationException(
                $"Fault injection for {serviceName} requires {variable}={containerName} and endpoint {Loopback}:{publishedPort}.");
        }

        // Never unlink the lock file: other processes may already be waiting on the same file.
        var lockDirectory = OperatingSystem.IsLinux() ? "/tmp" : Path.GetTempPath();
        var lockPath = Path.Combine(lockDirectory, $"{containerName}.fault-injection.lock");
        FileStream? fileLock = null;
        await WaitUntilAsync(_ =>
        {
            try
            {
                fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return Task.FromResult(true);
            }
            catch (IOException)
            {
                return Task.FromResult(false);
            }
        }, TimeSpan.FromMinutes(5), $"exclusive access to {containerName}");

        try
        {
            var cli = await GetLocalCliAsync();
            var json = await RunCliAsync(cli, "container", "inspect", containerName);
            var id = ValidateContainer(json, serviceName, containerName, publishedPort, targetPort);
            return new ServiceLease(cli, id, fileLock!);
        }
        catch
        {
            fileLock!.Dispose();
            throw;
        }
    }

    internal static string ValidateContainer(string json, string serviceName, string containerName, int publishedPort, int targetPort)
    {
        using var document = JsonDocument.Parse(json);
        var container = document.RootElement.EnumerateArray().Single();
        var labels = container.GetProperty("Config").GetProperty("Labels");
        var configFiles = Label("com.docker.compose.project.config_files");
        var podmanConfigFiles = Label("io.podman.compose.project.config_files", configFiles);
        var workingDirectory = Label("com.docker.compose.project.working_dir");
        var podmanWorkingDirectory = Label("io.podman.compose.project.working_dir", workingDirectory);
        var hostConfig = container.GetProperty("HostConfig");
        var bindings = hostConfig.GetProperty("PortBindings");
        var id = container.GetProperty("Id").GetString();
        if (container.GetProperty("Name").GetString()?.TrimStart('/') != containerName ||
            Label("com.docker.compose.project") != ComposeProject ||
            Label("io.podman.compose.project", ComposeProject) != ComposeProject ||
            Label("com.docker.compose.service") != serviceName ||
            Label("io.podman.compose.service", serviceName) != serviceName ||
            Label("io.crestcreates.test-services.config") != "v1" ||
            !MatchesComposeFile(configFiles, workingDirectory) ||
            hostConfig.GetProperty("NetworkMode").GetString() == "host" ||
            !bindings.TryGetProperty($"{targetPort}/tcp", out var ports) ||
            ports.ValueKind != JsonValueKind.Array || ports.GetArrayLength() != 1 ||
            ports[0].GetProperty("HostIp").GetString() != Loopback ||
            ports[0].GetProperty("HostPort").GetString() != publishedPort.ToString() ||
            !container.GetProperty("State").GetProperty("Running").GetBoolean() ||
            id is not { Length: 64 } || !id.All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                $"Container {containerName} must be the running {ComposeProject}/{serviceName} service from {ComposeFile}, " +
                $"published at {Loopback}:{publishedPort}. See infra/README.md.");
        }

        return id;

        string? Label(string key, string? fallback = null)
        {
            if (labels.ValueKind != JsonValueKind.Object)
                return null;
            return labels.TryGetProperty(key, out var value) ? value.GetString() : fallback;
        }
    }

    private static bool MatchesComposeFile(string? configFiles, string? workingDirectory)
    {
        if (configFiles is null || workingDirectory is null || configFiles.Contains(',') ||
            !Path.IsPathFullyQualified(workingDirectory) || Path.GetFileName(workingDirectory) != "infra")
            return false;

        // Docker Compose records absolute paths; podman-compose records the CLI's relative paths.
        var normalized = configFiles.Replace('\\', '/');
        return normalized == Path.Combine(workingDirectory, ComposeFile).Replace('\\', '/') ||
            normalized == "infra/" + ComposeFile ||
            (normalized == ComposeFile && Path.GetFileName(workingDirectory) == "infra");
    }

    public sealed class ServiceLease : IAsyncDisposable
    {
        private readonly string[] _cli;
        private readonly string _id;
        private readonly FileStream _fileLock;
        private bool _needsRecovery;

        internal ServiceLease(string[] cli, string id, FileStream fileLock)
        {
            _cli = cli;
            _id = id;
            _fileLock = fileLock;
        }

        public Task StopAsync()
        {
            ObjectDisposedException.ThrowIf(_fileLock.SafeFileHandle.IsClosed, this);
            // A failed CLI call can leave the container stopped. Preserve the recovery obligation.
            _needsRecovery = true;
            return RunCliAsync(_cli, "container", "stop", "--time", "10", _id);
        }

        public async Task RecoverAsync(Func<Task> waitForReady)
        {
            ObjectDisposedException.ThrowIf(_fileLock.SafeFileHandle.IsClosed, this);
            if (!_needsRecovery)
                return;

            await RunCliAsync(_cli, "container", "start", _id);
            await waitForReady();
            _needsRecovery = false;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_needsRecovery)
                    await RunCliAsync(_cli, "container", "start", _id);
            }
            finally
            {
                _needsRecovery = false;
                _fileLock.Dispose();
            }
        }
    }

    /// <summary>在容器内创建或清理测试夹具的一次性资源。</summary>
    public static async Task ExecAsync(string container, params string[] command)
    {
        if (string.IsNullOrEmpty(container) || !char.IsAsciiLetterOrDigit(container[0]) ||
            container.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')))
            throw new ArgumentException("Expected a container name or ID.", nameof(container));

        var cli = await GetLocalCliAsync();
        await RunCliAsync(cli, ["exec", container, .. command]);
    }

    public static Task WaitTcpAsync(string host, int port, TimeSpan timeout) => WaitUntilAsync(async cancellation =>
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellation);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }, timeout, $"TCP endpoint {host}:{port}");

    public static Task WaitPostgresAsync(string connectionString, TimeSpan timeout)
    {
        var probeConnection = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 2 };
        return WaitUntilAsync(async cancellation =>
        {
            try
            {
                await using var probe = new NpgsqlConnection(probeConnection.ConnectionString);
                await probe.OpenAsync(cancellation);
                // A single accept is not enough: PG may still be in its startup window and can
                // terminate fresh connections (57P01). Require two consecutive successful probes.
                await Task.Delay(500, cancellation);
                await using var confirm = new NpgsqlConnection(probeConnection.ConnectionString);
                await confirm.OpenAsync(cancellation);
                return true;
            }
            catch (NpgsqlException)
            {
                return false;
            }
        }, timeout, "PostgreSQL");
    }

    public static async Task WaitUntilAsync(Func<CancellationToken, Task<bool>> ready, TimeSpan timeout, string description)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (await ready(deadline.Token).WaitAsync(deadline.Token))
                    return;
                await Task.Delay(500, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out after {timeout} waiting for {description}.");
        }
    }

    private static async Task<string[]> GetLocalCliAsync()
    {
        var executable = await CliName.Value;
        return Path.GetFileNameWithoutExtension(executable) switch
        {
            "podman" => [executable, "--remote=false"],
            "docker" when !OperatingSystem.IsWindows() => [executable, "--host", LocalDockerSocket()],
            _ => throw new InvalidOperationException("Use the local podman or docker CLI on the test host.")
        };
    }

    private static string LocalDockerSocket()
    {
        var socket = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (string.IsNullOrEmpty(socket))
            return "unix:///var/run/docker.sock";
        if (!socket.StartsWith("unix:///", StringComparison.Ordinal))
            throw new InvalidOperationException("Test service control requires a local Docker Unix socket.");
        return socket;
    }

    private static async Task<string> RunCliAsync(string[] cli, params string[] args)
    {
        var environment = new Dictionary<string, string?> { ["DOCKER_CONTEXT"] = null };
        if (cli[0] == "podman" || Path.GetFileNameWithoutExtension(cli[0]) == "podman")
        {
            environment["CONTAINER_HOST"] = null;
            environment["DOCKER_HOST"] = null;
        }
        var result = await RunProcessAsync(cli[0], [.. cli.Skip(1), .. args], CliTimeout, environment);
        if (result.ExitCode != 0)
        {
            var output = string.Join(' ', result.Output.Split('\n').TakeLast(20)).Trim();
            throw new InvalidOperationException($"{cli[0]} {args[0]} failed (exit {result.ExitCode}): {output}");
        }
        return result.Output;
    }

    private static async Task<string> DetectCliAsync()
    {
        if (Environment.GetEnvironmentVariable("CREST_TEST_CONTAINER_CLI") is { Length: > 0 } configured)
            return configured;
        foreach (var candidate in new[] { "podman", "docker" })
        {
            try
            {
                if ((await RunProcessAsync(candidate, ["--version"], TimeSpan.FromSeconds(5))).ExitCode == 0)
                    return candidate;
            }
            catch (Exception ex) when (ex is Win32Exception or TimeoutException)
            {
                // Try the other local CLI.
            }
        }

        throw new InvalidOperationException(
            "Neither podman nor docker CLI is available; set CREST_TEST_CONTAINER_CLI to the local CLI.");
    }

    public static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName,
        IEnumerable<string> arguments, TimeSpan timeout, IReadOnlyDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (workingDirectory is not null)
            startInfo.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null)
                    startInfo.Environment.Remove(key);
                else
                    startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            throw new InvalidOperationException($"Could not start '{fileName}': {exception.Message}", exception);
        }
        using var deadline = new CancellationTokenSource(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr).WaitAsync(deadline.Token);
            return (process.ExitCode, await stdout + await stderr);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                using var reapDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(reapDeadline.Token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException)
            {
                throw new TimeoutException($"Process '{fileName}' exceeded {timeout} and could not be reaped.", ex);
            }
            throw new TimeoutException($"Process '{fileName}' exceeded {timeout}.");
        }
    }
}
