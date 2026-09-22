namespace MacAC.Extensibility.Hosting;

/// <summary>One chat-bar command typed by the player and routed to a verb owner.</summary>
public readonly record struct SlashCommand(
    string Verb,
    string Arguments,
    string RawText);

public interface ISlashCommandRegistry
{
    IDisposable Register(string verb, Action<SlashCommand> handler);
}

/// <summary>Accepts registrations but never routes anything to them.</summary>
public sealed class MuteSlashCommandRegistry : ISlashCommandRegistry
{
    public static MuteSlashCommandRegistry Instance { get; } = new();

    private MuteSlashCommandRegistry()
    {
    }

    public IDisposable Register(string verb, Action<SlashCommand> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verb);
        ArgumentNullException.ThrowIfNull(handler);
        return Nothing.Instance;
    }

    private sealed class Nothing : IDisposable
    {
        public static Nothing Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
