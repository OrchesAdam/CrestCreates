using System;
using System.Collections.Generic;
using FluentAssertions;

namespace CrestCreates.CapabilityEndpoint.AotFixture.Tests;

/// <summary>
/// Bounded native-server startup: owns the child process until it is handed to
/// the caller. Every failure path (early exit, listen timeout, listening-line
/// assertion, bind race) reaps the process before rethrowing; only an
/// "address already in use" bind race decides whether another attempt is made,
/// never whether cleanup happens.
/// </summary>
internal static class NativeServerLauncher
{
    public static async Task<(NativeServerProcess Server, string BaseUrl)> StartWithBoundedBindRetryAsync(
        string executablePath,
        string workingDirectory,
        TimeSpan listenTimeout,
        int maxAttempts = 3,
        IReadOnlyList<string>? runArguments = null)
    {
        string? lastOutput = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var port = NativeAotGateSupport.ReserveLoopbackPort();
            var baseUrl = $"http://127.0.0.1:{port}";
            var server = runArguments is null
                ? NativeServerProcess.Start(executablePath, workingDirectory, baseUrl)
                : NativeServerProcess.Start(executablePath, workingDirectory, runArguments);
            try
            {
                await NativeGateWaiter.WaitForListeningLineAsync(server, listenTimeout);
                server.StandardOutput.Should().Contain(
                    $"Now listening on: {baseUrl}",
                    "the listening line must come from this child process on the port reserved for this run");
                return (server, baseUrl);
            }
            catch (Exception exception)
            {
                var standardOutput = server.StandardOutput;
                var standardError = server.StandardError;
                lastOutput = standardOutput + Environment.NewLine + standardError;
                var lostBindRace = lastOutput.Contains(
                    "address already in use", StringComparison.OrdinalIgnoreCase);
                var processId = server.ProcessId;
                var exitCode = await server.ReapAsync();
                await server.DisposeAsync();

                if (!lostBindRace || attempt == maxAttempts)
                {
                    throw new NativeFixtureStartupException(
                        $"Native fixture failed to become ready on attempt {attempt}/{maxAttempts}: {exception.Message}",
                        standardOutput,
                        standardError,
                        processId,
                        exitCode);
                }
            }
        }

        throw new NativeFixtureStartupException(
            $"Native fixture failed to bind a loopback port after {maxAttempts} bounded attempts.",
            lastOutput ?? string.Empty,
            string.Empty,
            processId: null,
            processExitCode: null);
    }
}
