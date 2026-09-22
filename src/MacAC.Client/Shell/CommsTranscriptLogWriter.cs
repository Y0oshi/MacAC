using MacAC.Cockpit.Panels.Chat;
using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell;

public sealed class CommsTranscriptLogWriter(SessionTranscript log)
{
    private readonly SessionTranscript _trace = log ?? throw new ArgumentNullException(nameof(log));
    private ChatTranscript? _src;

    public void Attach(ChatTranscript src)
    {
        ArgumentNullException.ThrowIfNull(src);
        Unfasten();
        _src = src;
        src.EntryAppended += Write;
    }

    public void Unfasten()
    {
        if (_src is null)
            return;

        _src.EntryAppended -= Write;
        _src = null;
    }

    private void Write(ChatRow listing)
    {
        var src = _src;
        if (src is null)
            return;

        bool stamped = src.ReadoutTimestampsSrc?.Invoke() == true;

        _trace.Write(
            stamped ? ChatTranscript.ComposeStampStem(listing.Received) : null,
            ChatModel.ComposeListing(listing));
    }
}
