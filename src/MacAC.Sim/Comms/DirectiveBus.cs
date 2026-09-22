namespace MacAC.Sim.Comms;

/// <summary>Publishes typed commands to whoever registered for them.</summary>
public interface IDirectiveBus
{
    void Publish<T>(T directive) where T : notnull;
}

public interface IPluginDirectiveBus : IDirectiveBus
{
    bool TryHndExtensionDirective(string directiveStroke);
}

/// <summary>A bus that swallows everything, for hosts without a session.</summary>
public sealed class NullDirectiveBus : IDirectiveBus
{
    /// <summary>Shared singleton - the bus is stateless.</summary>
    public static readonly NullDirectiveBus Instance = new();

    private NullDirectiveBus()
    {
    }

    public void Publish<T>(T directive) where T : notnull
    {
    }
}

/// <summary>One handler per command type; an unhandled publish is logged and dropped.</summary>
public sealed class OnlineDirectiveBus : IDirectiveBus
{
    private readonly Dictionary<Type, Delegate> _handlers = new();

    public void Register<T>(Action<T> handler) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(typeof(T), handler))
            throw new InvalidOperationException($"A handler for command type {typeof(T).FullName} is by now registered");
    }

    public void Publish<T>(T directive) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(directive);
        if (_handlers.TryGetValue(typeof(T), out Delegate? handler))
            ((Action<T>)handler).Invoke(directive);
        else
            Console.WriteLine($"[OnlineDirectiveBus] no handler registered for {typeof(T).FullName}; dropping");
    }

    public void Clear() => _handlers.Clear();
}
