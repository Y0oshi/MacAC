using MacAC.Extensibility.Hosting;

namespace MacAC.Client.Extensions;

public sealed class SerilogBridge(Serilog.ILogger trace) : IExtensionLog
{
    private readonly Serilog.ILogger _trace = trace;

    public void Info(string msg) => _trace.Information("{Message}", msg);
    public void Warn(string msg) => _trace.Warning("{Message}", msg);
    public void Error(string msg, Exception? exception = null) => _trace.Error(exception, "{Message}", msg);
}
