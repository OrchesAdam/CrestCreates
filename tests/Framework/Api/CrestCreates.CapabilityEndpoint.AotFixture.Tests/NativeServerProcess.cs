using System.Diagnostics;
using System.Text;

namespace CrestCreates.CapabilityEndpoint.AotFixture.Tests;

/// <summary>
/// Owns a single native fixture process launched by a gate test. Only the
/// process handle captured at start is ever signalled — never a process found
/// by name — so a reap can never touch unrelated system processes.
/// </summary>
internal sealed class NativeServerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly object _outputLock = new();
    private readonly StringBuilder _standardOutput = new();
    private readonly StringBuilder _standardError = new();

    private NativeServerProcess(Process process)
    {
        _process = process;
    }

    public int ProcessId => _process.Id;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode => HasExited ? SafeExitCode() : null;

    public string StandardOutput
    {
        get
        {
            lock (_outputLock)
                return _standardOutput.ToString();
        }
    }

    public string StandardError
    {
        get
        {
            lock (_outputLock)
                return _standardError.ToString();
        }
    }

    public static NativeServerProcess Start(
        string executablePath, string workingDirectory, string baseUrl)
    {
        return Start(executablePath, workingDirectory, new[] { "--urls", baseUrl });
    }

    public static NativeServerProcess Start(
        string executablePath, string workingDirectory, IReadOnlyList<string> runArguments)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Native fixture executable was not found: {executablePath}", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in runArguments)
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo };
        var wrapper = new NativeServerProcess(process);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;
            lock (wrapper._outputLock)
                wrapper._standardOutput.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;
            lock (wrapper._outputLock)
                wrapper._standardError.AppendLine(args.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return wrapper;
    }

    public async Task<int?> ReapAsync(TimeSpan? timeout = null)
    {
        var budget = timeout ?? TimeSpan.FromSeconds(15);
        if (!HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        try
        {
            using var timeoutSource = new CancellationTokenSource(budget);
            await _process.WaitForExitAsync(timeoutSource.Token);
            // Drain pending asynchronous output handlers so evidence capture
            // observes every stdout/stderr line the process wrote before exit.
            _process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return SafeExitCode();
    }

    private int? SafeExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ReapAsync();
        _process.Dispose();
    }
}

internal static class NativeGateWaiter
{
    /// <summary>
    /// Waits until the child reports its own listening endpoint. Fails fast if
    /// the process exits first (bounded, no infinite wait).
    /// </summary>
    public static async Task WaitForListeningLineAsync(
        NativeServerProcess server, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (server.HasExited)
            {
                throw new NativeFixtureStartupException(
                    $"Native fixture exited with code {server.ExitCode} before reporting a listening endpoint.",
                    server.StandardOutput,
                    server.StandardError);
            }

            if (server.StandardOutput.Contains("Now listening on:", StringComparison.Ordinal))
                return;

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Native fixture did not report a listening endpoint within {timeout}.");
    }

    /// <summary>
    /// Polls a read-only endpoint until it answers successfully. Fails fast if
    /// the process exits; fails at the deadline with a bounded timeout.
    /// </summary>
    public static async Task WaitForHttpReadyAsync(
        NativeServerProcess server, HttpClient http, string readinessPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastProbeError = null;
        while (DateTime.UtcNow < deadline)
        {
            if (server.HasExited)
            {
                throw new NativeFixtureStartupException(
                    $"Native fixture exited with code {server.ExitCode} before serving requests.",
                    server.StandardOutput,
                    server.StandardError);
            }

            try
            {
                using var response = await http.GetAsync(readinessPath);
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    return;
                lastProbeError = new InvalidOperationException(
                    $"Readiness probe returned HTTP {(int)response.StatusCode}.");
            }
            catch (HttpRequestException ex)
            {
                lastProbeError = ex;
            }
            catch (TaskCanceledException ex)
            {
                lastProbeError = ex;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Native fixture did not serve an HTTP readiness response within {timeout}. " +
            $"Last probe error: {lastProbeError?.Message ?? "none"}");
    }
}
