using System.Diagnostics;

namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

public static class DotNetProcess
{
    public static async Task<DotNetProcessResult> RunAsync(
        string workingDirectory,
        string arguments,
        IDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (environmentVariables != null)
            foreach (var (key, value) in environmentVariables)
                psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(3);
        var waitForExitTask = Task.Run(() => process.WaitForExit());
        var completed = await Task.WhenAny(waitForExitTask, Task.Delay(effectiveTimeout));

        if (completed != waitForExitTask)
        {
            try { process.Kill(true); } catch { }
        }

        var output = await outputTask;
        var error = await errorTask;

        return new DotNetProcessResult(
            process.HasExited ? process.ExitCode : -1,
            output,
            error);
    }
}
