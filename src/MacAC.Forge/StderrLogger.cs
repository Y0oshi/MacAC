using Microsoft.Extensions.Logging;

namespace MacAC.Forge;

public sealed class StderrLogger(string bucket) : ILogger
{
    public IDisposable? BeginScope<TState>(TState phase) where TState : notnull => null;

    public bool IsEnabled(LogLevel tier) => tier >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel tier,
        EventId signalIdent,
        TState phase,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(tier))
            return;
        Console.Error.WriteLine($"[{tier}] {bucket}: {formatter(phase, exception)}");
        if (exception is not null)
            Console.Error.WriteLine(exception);
    }
}

public sealed class StderrLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string bucketLabel) => new StderrLogger(bucketLabel);

    public void Dispose()
    {
    }
}
