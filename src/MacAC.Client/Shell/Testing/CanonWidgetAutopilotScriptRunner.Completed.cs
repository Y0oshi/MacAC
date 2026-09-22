using System.Globalization;
using MacAC.Cockpit.Input;

namespace MacAC.Client.Shell.Testing;

public sealed partial class CanonWidgetAutopilotScriptRunner
{
    public bool Completed { get; private set; }

    public void Tick(double diffSecs)
    {
        if (Completed || _destroyed) return;

        if (double.IsFinite(diffSecs) && diffSecs > 0d)
        {
            double appendMsec = diffSecs * 1000d;
            double adjustedMsec = appendMsec - _directivePassedCompensationMsec;
            double upcomingPassedMsec = _directivePassedMsec + adjustedMsec;
            double upcomingCompensationMsec = (upcomingPassedMsec - _directivePassedMsec) - adjustedMsec;
            if (_engagedOrdinal == _ordinal &&
                double.IsFinite(appendMsec) &&
                double.IsFinite(upcomingPassedMsec) &&
                double.IsFinite(upcomingCompensationMsec))
            {
                _directivePassedMsec = upcomingPassedMsec;
                _directivePassedCompensationMsec = upcomingCompensationMsec;
            }
        }

        if (!_begun)
        {
            _begun = true;
            if (_pullProblem is not null)
            {
                _trace(_pullProblem);
                Completed = true;
                return;
            }

            if (_printOnBegin)
                _trace(_sensor.PrintPhrase());

            if (_commands.Count is 0)
            {
                Completed = true;
                return;
            }

            _trace($"running {_commands.Count} UI probe command(s)");
        }

        int guard = 0;
        while (!Completed && _ordinal < _commands.Count && guard++ < 8)
        {
            if (_engagedOrdinal != _ordinal)
            {
                _engagedOrdinal = _ordinal;
                _directivePassedMsec = 0d;
                _directivePassedCompensationMsec = 0d;
            }

            ScriptDirective directive = _commands[_ordinal];
            bool finished = Perform(directive);
            if (!finished) break;
            bool yieldFollowingRescale = directive.Parts.Length > 0
                && string.Equals(
                    directive.Parts[0],
                    "resize",
                    StringComparison.OrdinalIgnoreCase);
            ++_ordinal;
            _engagedOrdinal = -1;
            if (yieldFollowingRescale)
                break;
        }

        if (_ordinal >= _commands.Count && !Completed)
        {
            FreePinnedFeeds();
            Completed = true;
            _trace("UI probe script complete");
        }
    }

    public void Dispose()
    {
        if (_destroyed) return;
        if (_checkpoint is not null)
        {
            _runtime?.AbortCheckpoint(_checkpoint);
            _checkpoint = null;
            _checkpointDirectiveOrdinal = -1;
        }
        FreePinnedFeeds();
        _destroyed = true;
    }

    private bool Perform(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length is 0) return true;

        string verb = p[0].ToLowerInvariant();
        return verb switch
        {
            "dump" => DoPrint(),
            "click" => DoPress(directive),
            "hover" => DoHover(directive),
            "mousemove" => DoPointerRelocate(directive),
            "doubleclick" => DoDoublePress(directive),
            "drag" => DoPull(directive),
            "wait" => DoPause(directive),
            "sleep" => DoSleep(directive),
            "assert" => DoInsist(directive),
            "command" => DoDirective(directive),
            "input" => DoFeed(directive),
            "mouselook" => DoPointerGaze(directive),
            "checkpoint" => DoCheckpoint(directive),
            "renderpack" => DoRasterizeBundle(directive),
            "resize" => DoRescale(directive),
            "screenshot" => DoScreenshot(directive),
            "close-client" => DoShutClient(directive),
            _ => Stop(directive, $"unknown command '{p[0]}'"),
        };
    }

    private static bool TryNormalizeRasterizeBundlePreset(
        string val,
        out string preset)
    {
        preset = val.ToLowerInvariant();
        if (preset == "off")
            preset = "retail";
        return preset is "retail" or "low" or "medium" or "high" or "auto";
    }

    private bool PauseOrTimeout(ScriptDirective directive, int timeoutMsec, string caption)
    {
        return !HasExceeded(timeoutMsec) ? false : Stop(directive, $"timed out waiting for {caption}");
    }

    private bool HasPassed(int intervalMsec)
    {
        double passedMsec = _directivePassedMsec;
        if (!double.IsFinite(passedMsec)) return false;
        if (passedMsec >= intervalMsec) return true;

        return intervalMsec - passedMsec <= TimingToleranceMsec(intervalMsec);
    }

    private bool HasExceeded(int intervalMsec)
    {
        double passedMsec = _directivePassedMsec;
        return !double.IsFinite(passedMsec) || passedMsec <= intervalMsec ? false : passedMsec - intervalMsec > TimingToleranceMsec(intervalMsec);
    }

    private static double TimingToleranceMsec(int intervalMsec)
        => Math.Max(1e-9d, Math.Abs(intervalMsec) * 1e-12d);

    private bool Stop(ScriptDirective directive, string msg)
    {
        _trace($"line {directive.LineNumber}: {msg}; command: {directive.Text}");
        FreePinnedFeeds();
        Completed = true;
        return false;
    }

    private void FreePinnedFeeds()
    {
        if (_pinnedFeeds.Count is 0 || _setFeedPinned is null)
            return;

        FeedAct[] capture = new FeedAct[_pinnedFeeds.Count];
        _pinnedFeeds.CopyTo(capture);
        _pinnedFeeds.Clear();
        foreach (FeedAct act in capture)
            _setFeedPinned(act, false);
    }

    private static string StripComment(string stroke)
    {
        int digest = stroke.IndexOf('#');
        int slashes = stroke.IndexOf("//", StringComparison.Ordinal);
        int cut = -1;
        if (digest >= 0) cut = digest;
        if (slashes >= 0) cut = cut >= 0 ? Math.Min(cut, slashes) : slashes;
        return cut >= 0 ? stroke[..cut] : stroke;
    }

    private static string[] Split(string stroke)
    {
        return stroke.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static GearDragSource? DecodeSrc(string[] pieces, int begin)
    {
        for (int idx = begin; idx < pieces.Length; ++idx)
            if (TryDecodeSrc(pieces[idx], out var src))
                return src;
        return null;
    }

    private static bool TryDecodeSrc(string val, out GearDragSource src)
    {
        switch (val.ToLowerInvariant())
        {
            case "inventory":
            case "pack":
            case "backpack":
                src = GearDragSource.Inventory;
                return true;
            case "shortcut":
            case "shortcutbar":
            case "toolbar":
                src = GearDragSource.ShortcutBar;
                return true;
            case "equipment":
            case "equip":
            case "paperdoll":
                src = GearDragSource.Equipment;
                return true;
            case "ground":
                src = GearDragSource.Ground;
                return true;
            default:
                src = default;
                return false;
        }
    }

    private static bool TryDecodeUInt(string val, out uint decoded)
    {
        return val.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(val[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out decoded)
            : uint.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out decoded);
    }

    private static bool TryDecodeInt(string val, out int decoded)
    {
        return int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out decoded);
    }

    private static bool TryDecodeFloat(string val, out float decoded)
    {
        return float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out decoded);
    }

    private static int TimeoutMsec(string[] pieces, int begin, int defaultMsec)
    {
        for (int idx = begin; idx < pieces.Length; ++idx)
            if (TryDecodeInt(pieces[idx], out int msec) && msec >= 0)
                return msec;
        return defaultMsec;
    }
}
