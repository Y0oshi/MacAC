using Microsoft.Extensions.Logging;

namespace MacAC.Client.Rigging;

/// <summary>Routes library logging into the engine's own one-line probe sink.</summary>
internal sealed class ProbeLogger(Action<string> probe) : ILogger
{
    private readonly Action<string> _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    public IDisposable BeginScope<TState>(TState phase) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel traceTier) => traceTier >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel traceTier,
        EventId signalIdent,
        TState phase,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(traceTier)) return;
        ArgumentNullException.ThrowIfNull(formatter);
        string line = formatter(phase, exception);
        _probe(exception is null ? line : $"{line}: {exception.GetType().Name}: {exception.Message}");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
