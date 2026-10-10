using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CrestCreates.CapabilityEndpoint.AotFixture.Tests;

internal static class NativeAotGateSupport
{
    public static string FindRepoRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CrestCreates.slnx")))
                return current.FullName;
        }

        throw new InvalidOperationException("Could not locate the CrestCreates repository root.");
    }

    public static string EvidenceFilePath(string evidenceName)
    {
        var directory = Path.Combine(
            FindRepoRoot(), "tests", "artifacts", "aot-native-evidence");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, evidenceName + ".json");
    }

    public static int ReserveLoopbackPort()
    {
        // Port 0 lets the OS hand out a free ephemeral port. The listener is
        // released immediately; the native fixture must then win the bind or
        // the readiness wait fails explicitly (bounded retry happens upstream).
        while (true)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (port != 5432)
                return port;
        }
    }

    public static string ResolveSha()
    {
        var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(sha))
            return sha!;

        try
        {
            var startInfo = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = FindRepoRoot()
            };
            using var process = Process.Start(startInfo);
            if (process is null)
                return "unknown";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return output.Length > 0 ? output : "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    public static async Task<ProcessResult> RunProcessAsync(
        string fileName, string arguments, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.Start();
        var processId = process.Id;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        var duration = DateTime.UtcNow - startedAt;
        // Drain captured output even on timeout so the partial publish transcript
        // is preserved for the failure evidence instead of being discarded.
        var output = string.Concat(await stdout, Environment.NewLine, await stderr);
        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            output,
            duration,
            timedOut,
            processId);
    }
}

internal sealed record ProcessResult(
    int ExitCode,
    string Output,
    TimeSpan Duration,
    bool TimedOut = false,
    int ProcessId = 0);

internal sealed class EvidenceCollector
{
    private readonly Dictionary<string, object?> _fields = new(StringComparer.Ordinal);

    public EvidenceCollector(string evidenceName)
    {
        FilePath = NativeAotGateSupport.EvidenceFilePath(evidenceName);
    }

    public string FilePath { get; }

    public void Add(string key, object? value) => _fields[key] = value;

    /// <summary>
    /// Best-effort evidence write. Evidence must never mask the gate's own
    /// pass/fail signal, so filesystem failures are swallowed.
    /// </summary>
    public void Write(string result, string? failure)
    {
        _fields["result"] = result;
        _fields["failure"] = failure;
        _fields["writtenUtc"] = DateTime.UtcNow.ToString("o");
        try
        {
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(_fields, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
        }
    }
}

internal sealed class NativeFixtureStartupException : Exception
{
    public NativeFixtureStartupException(
        string message,
        string standardOutput,
        string standardError,
        int? processId = null,
        int? processExitCode = null)
        : base(message)
    {
        StandardOutput = standardOutput;
        StandardError = standardError;
        ProcessId = processId;
        ProcessExitCode = processExitCode;
    }

    public string StandardOutput { get; }

    public string StandardError { get; }

    /// <summary>本次启动的子进程 PID（清理后保留，用于证明进程已回收）。</summary>
    public int? ProcessId { get; }

    /// <summary>清理后的子进程退出码（null 表示无法回收）。</summary>
    public int? ProcessExitCode { get; }
}
