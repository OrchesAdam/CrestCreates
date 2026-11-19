using System.Diagnostics;
using System.Net.Sockets;

namespace CrestCreates.TestServices;

/// <summary>
/// 故障注入用服务停启：经 podman/docker CLI 操作专属长驻容器（不经 Testcontainers exec 通道）。
/// 只允许停启测试专属实例，不得指向共享演示服务。
/// </summary>
public static class TestServiceControl
{
    private static readonly Lazy<string> CliName = new(DetectCli);

    public static Task StopAsync(string container) => RunCliAsync("stop", container);

    public static Task StartAsync(string container) => RunCliAsync("start", container);

    /// <summary>在容器内执行命令（如 rabbitmqctl），用于一次性隔离资源（vhost 等）的创建/清理。</summary>
    public static Task ExecAsync(string container, params string[] command)
    {
        var args = new string[command.Length + 2];
        args[0] = "exec";
        args[1] = container;
        Array.Copy(command, 0, args, 2, command.Length);
        return RunCliAsync(args);
    }

    public static async Task WaitTcpAsync(string host, int port, TimeSpan timeout)
    {
        await WaitUntilAsync(async () =>
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }, timeout);
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> ready, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await ready())
                return;
            await Task.Delay(500);
        }

        throw new TimeoutException($"Service did not become ready within {timeout}.");
    }

    private static Task RunCliAsync(string verb, string container)
        => RunCliAsync(new[] { verb, container });

    private static async Task RunCliAsync(string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = CliName.Value,
                Arguments = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"'{CliName.Value} {string.Join(" ", args)}' failed (exit {process.ExitCode}): {stderr.Trim()}");
    }

    private static string DetectCli()
    {
        if (Environment.GetEnvironmentVariable("CREST_TEST_CONTAINER_CLI") is { Length: > 0 } configured)
            return configured;
        foreach (var candidate in new[] { "podman", "docker" })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                probe!.WaitForExit(5000);
                if (probe.ExitCode == 0)
                    return candidate;
            }
            catch (Exception)
            {
                // candidate not installed
            }
        }

        throw new InvalidOperationException(
            "Neither podman nor docker CLI is available; set CREST_TEST_CONTAINER_CLI to a compatible CLI.");
    }
}