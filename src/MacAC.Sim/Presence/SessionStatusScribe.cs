using System.Globalization;
using System.Text.Json;

namespace MacAC.Sim.Presence;

public sealed class SessionStatusScribe
{
    private const int VocabularyVer = 1;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string? _trail;
    private readonly TimeProvider _clock;
    private readonly object _latch = new();
    private bool _folderPrimed;
    private bool _off;
    private bool _connected;
    private bool _exited;

    public SessionStatusScribe(string? trail, TimeProvider? momentSupplier = null)
    {
        _trail = string.IsNullOrWhiteSpace(trail) ? null : Path.GetFullPath(trail);
        _clock = momentSupplier ?? TimeProvider.System;
    }

    public bool IsEnabled => _trail is not null && !_off;

    public void Started(string sessIdent) => Write(Signal("started", sessIdent));

    public void CharacterList(string sessIdent, OnlineSessionRosterNotice lineup)
    {
        ArgumentNullException.ThrowIfNull(lineup);
        if (!IsEnabled)
            return;
        Write(Signal("characterList", sessIdent,
            ("accountName", lineup.AccountName),
            ("slotCount", lineup.SlotCount),
            ("characters", lineup.Entries.Select(static entry => new { id = entry.Id, name = entry.Name, secondsGreyedOut = entry.SecondsGreyedOut }).ToArray())));
    }

    public void EnteredWorld(string sessIdent, uint toonIdent, string toonLabel)
    {
        Write(Signal("enteredWorld", sessIdent, ("characterId", toonIdent), ("characterName", toonLabel)));
    }

    public void ToonBuilt(string sessIdent, uint oid, string label)
    {
        Write(Signal("characterCreated", sessIdent, ("guid", oid), ("name", label)));
    }

    public void CreationFailed(string sessIdent, uint code, string cause, string label)
    {
        Write(Signal("creationFailed", sessIdent, ("code", code), ("reason", cause), ("name", label)));
    }

    public void ExtensionFetched(string sessIdent, string plugin) => Write(Signal("pluginLoaded", sessIdent, ("plugin", plugin)));

    public void ExtensionFailed(string sessIdent, string plugin, string problem)
    {
        Write(Signal("pluginFailed", sessIdent, ("plugin", plugin), ("error", problem)));
    }

    public void SignInDirectiveFailed(string sessIdent, int directiveOrdinal, string directive, string problem)
    {
        Write(Signal("loginCommandFailed", sessIdent, ("commandIndex", directiveOrdinal), ("command", directive), ("error", problem)));
    }

    public void Connected(string sessIdent)
    {
        if (!IsEnabled)
            return;
        lock (_latch)
        {
            if (_off || _exited)
                return;
            if (_connected && !ShutConnection(sessIdent, "reconnect"))
                return;
            if (Place(Signal("connected", sessIdent)))
                _connected = true;
        }
    }

    public void Disconnected(string sessIdent, string cause)
    {
        if (!IsEnabled)
            return;
        lock (_latch)
        {
            if (_off || _exited)
                return;
            if (Place(Signal("disconnected", sessIdent, ("reason", cause))))
                _connected = false;
        }
    }

    public void Exited(string sessIdent, int code, string cause)
    {
        if (!IsEnabled)
            return;
        lock (_latch)
        {
            if (_off || _exited)
                return;
            if (_connected && !ShutConnection(sessIdent, "process-exit"))
                return;
            if (Place(Signal("exited", sessIdent, ("code", code), ("reason", cause))))
                _exited = true;
        }
    }

    // The common envelope followed by the event's own fields, in order
    private Dictionary<string, object?> Signal(string sort, string sessIdent, params (string Key, object? Value)[] fields)
    {
        var stroke = new Dictionary<string, object?>
        {
            ["v"] = VocabularyVer,
            ["e"] = sort,
            ["t"] = _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            ["sessionId"] = sessIdent,
        };
        foreach ((string tag, object? val) in fields)
            stroke[tag] = val;
        return stroke;
    }

    // Writes the implied disconnect; false when the write failed (and the stream is now off)
    private bool ShutConnection(string sessIdent, string cause)
    {
        if (!Place(Signal("disconnected", sessIdent, ("reason", cause))))
            return false;
        _connected = false;
        return true;
    }

    private void Write(Dictionary<string, object?> stroke)
    {
        if (_trail is null || _off)
            return;
        lock (_latch)
        {
            if (_off || _exited)
                return;
            _ = Place(stroke);
        }
    }

    private bool Place(Dictionary<string, object?> stroke)
    {
        string trail = _trail!;
        try
        {
            if (!_folderPrimed)
            {
                string? folder = Path.GetDirectoryName(trail);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                _folderPrimed = true;
            }

            string json = JsonSerializer.Serialize(stroke, Json);
            using FileStream flow = new FileStream(trail, FileMode.Append, FileAccess.Write, FileShare.Read);
            using StreamWriter writer = new StreamWriter(flow);
            writer.WriteLine(json);
            writer.Flush();
            return true;
        }
        catch (Exception problem) when (Recoverable(problem))
        {
            SwitchOff(trail, problem);
            return false;
        }
    }

    private void SwitchOff(string trail, Exception problem)
    {
        _off = true;
        try
        {
            Console.Error.WriteLine(
                $"[status-writer] disabling status stream at '{trail}' after a "
                + $"write failure ({problem.GetType().Name}: {problem.Message}); no "
                + "further events for this session will be written");
        }
        catch (Exception probe) when (Recoverable(probe) || probe is ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static bool Recoverable(Exception problem)
    {
        return problem is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or System.Security.SecurityException or DirectoryNotFoundException;
    }
}
