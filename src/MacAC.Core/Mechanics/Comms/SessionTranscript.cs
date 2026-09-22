using System.Text;

namespace MacAC.Mechanics.Comms;

public readonly record struct ChatTranscriptResult(
    bool Opened,
    bool Closed,
    string Name,
    string? ClosedName);

/// <summary>The /log command's file: appended line by line, flushed as it goes.</summary>
public sealed class SessionTranscript : IDisposable
{
    private readonly string _trunk;
    private StreamWriter? _drain;

    public SessionTranscript(string baseFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFolder);
        _trunk = baseFolder;
    }

    public string? LatestLabel { get; private set; }

    public bool IsOpen => _drain is not null;

    public static string SecureExtension(string label) =>
        Path.GetExtension(label).Length is 0 ? label + ".txt" : label;

    public bool Open(string label, out string settledLabel)
    {
        settledLabel = string.Empty;
        Close();
        if (string.IsNullOrWhiteSpace(label))
            return false;

        settledLabel = SecureExtension(label.Trim());
        try
        {
            string trail = Path.IsPathRooted(settledLabel) ? settledLabel : Path.Combine(_trunk, settledLabel);
            if (Path.GetDirectoryName(trail) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);

            _drain = new StreamWriter(
                new FileStream(trail, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
            LatestLabel = settledLabel;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _drain = null;
            LatestLabel = null;
            return false;
        }
    }

    public bool Close()
    {
        if (_drain is null)
            return false;
        try
        {
            _drain.Dispose();
        }
        catch (IOException)
        {
        }
        _drain = null;
        LatestLabel = null;
        return true;
    }

    public void Write(string? stampStem, string? phrase)
    {
        if (_drain is not { } drain)
            return;
        try
        {
            drain.Write(stampStem);
            drain.Write(phrase);
            drain.Write('\n');
        }
        catch (IOException)
        {
        }
    }

    public void Dispose() => Close();
}
