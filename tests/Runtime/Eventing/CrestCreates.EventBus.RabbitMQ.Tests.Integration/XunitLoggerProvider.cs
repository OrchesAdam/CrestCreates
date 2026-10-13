using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

/// <summary>Bridges host logging into the xunit output so integration diagnostics are visible on failure.</summary>
internal sealed class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new XunitLogger(output, categoryName);

    public void Dispose()
    {
    }

    private sealed class XunitLogger(ITestOutputHelper output, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                output.WriteLine($"[{logLevel}] {category}: {formatter(state, exception)}");
                if (exception is not null)
                {
                    output.WriteLine($"    {exception.GetType().Name}: {exception.Message}");
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
