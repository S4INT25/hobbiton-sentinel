using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace FraudDetector.Infrastructure;

/// <summary>
/// A console logger provider that suppresses all log output for specific request paths.
/// Used to prevent high-frequency polling endpoints (e.g. /hangfire/stats) from flooding logs.
/// </summary>
public sealed class PathFilteredLoggerProvider(
    IHttpContextAccessor accessor,
    IOptionsMonitor<ConsoleLoggerOptions> options,
    IEnumerable<ConsoleFormatter> formatters) : ILoggerProvider
{
    private static readonly string[] SuppressedPaths = ["/hangfire/stats"];
    private readonly ConsoleLoggerProvider _inner = new(options, formatters);

    public ILogger CreateLogger(string categoryName) =>
        new PathFilteredLogger(accessor, _inner.CreateLogger(categoryName));

    public void Dispose() => _inner.Dispose();
}

file sealed class PathFilteredLogger(IHttpContextAccessor accessor, ILogger inner) : ILogger
{
    private static readonly string[] SuppressedPaths = ["/hangfire/stats"];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var path = accessor.HttpContext?.Request.Path.Value;
        if (path is not null &&
            SuppressedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return;

        inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
