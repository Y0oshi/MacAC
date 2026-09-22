using MacAC.Cockpit.Input;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Testing;

public sealed partial class CanonWidgetAutopilotScriptRunner
{
    private bool DoPrint()
    {
        _trace(_sensor.PrintPhrase());
        return true;
    }

    private bool DoPress(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length < 3) return Stop(directive, "usage: click element <datId> | click item <guid> [source] | click at <x> <y>");
        string mark = p[1].ToLowerInvariant();
        if (mark == "element")
        {
            return !TryDecodeUInt(p[2], out uint datIdent)
                ? Stop(directive, $"bad element id '{p[2]}'")
                : _sensor.PressElem(datIdent) || Stop(directive, "click element failed");
        }
        if (mark == "item")
        {
            return !TryDecodeUInt(p[2], out uint gearOid)
                ? Stop(directive, $"bad item guid '{p[2]}'")
                : _sensor.PressGear(gearOid, DecodeSrc(p, 3)) || Stop(directive, "click item failed");
        }
        if (mark == "at")
        {
            return p.Length < 4
                || !TryDecodeInt(p[2], out int x)
                || !TryDecodeInt(p[3], out int y)
                ? Stop(directive, "usage: click at <x> <y>")
                : _sensor.PressAtPt(x, y) || Stop(directive, "click at failed");
        }
        return Stop(directive, "usage: click element <datId> | click item <guid> [source] | click at <x> <y>");
    }

    private bool DoHover(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length < 3 || !string.Equals(p[1], "element", StringComparison.OrdinalIgnoreCase))
            return Stop(directive, "usage: hover element <datId>");
        return !TryDecodeUInt(p[2], out uint datIdent)
            ? Stop(directive, $"bad element id '{p[2]}'")
            : _sensor.HoverElem(datIdent) || Stop(directive, "hover element failed");
    }

    private bool DoPointerRelocate(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length is not 3)
            return Stop(directive, "usage: mousemove <x> <y>");
        if (!TryDecodeInt(p[1], out int x)) return Stop(directive, $"bad x '{p[1]}'");
        if (!TryDecodeInt(p[2], out int y)) return Stop(directive, $"bad y '{p[2]}'");
        _sensor.ShiftPointer(x, y);
        return true;
    }

    private bool DoDoublePress(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length < 3 || !string.Equals(p[1], "item", StringComparison.OrdinalIgnoreCase))
            return Stop(directive, "usage: doubleclick item <guid> [source]");
        return !TryDecodeUInt(p[2], out uint gearOid)
            ? Stop(directive, $"bad item guid '{p[2]}'")
            : _sensor.DoublePressGear(gearOid, DecodeSrc(p, 3)) || Stop(directive, "doubleclick item failed");
    }

    private bool DoPull(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length >= 6
            && string.Equals(p[1], "at", StringComparison.OrdinalIgnoreCase))
        {
            return !TryDecodeInt(p[2], out int x1) || !TryDecodeInt(p[3], out int y1)
                || !TryDecodeInt(p[4], out int x2) || !TryDecodeInt(p[5], out int y2)
                ? Stop(directive, "usage: drag at <x1> <y1> <x2> <y2>")
                : _sensor.PullAtPt(x1, y1, x2, y2)
                || Stop(directive, "drag at failed");
        }
        if (p.Length < 5 || !string.Equals(p[1], "item", StringComparison.OrdinalIgnoreCase))
            return Stop(directive, "usage: drag item <guid> element <datId> | drag item <guid> item <guid> | drag item <guid> outside <x> <y> | drag at <x1> <y1> <x2> <y2>");
        if (!TryDecodeUInt(p[2], out uint srcOid)) return Stop(directive, $"bad item guid '{p[2]}'");

        string mark = p[3].ToLowerInvariant();
        if (mark == "element")
        {
            return !TryDecodeUInt(p[4], out uint datIdent)
                ? Stop(directive, $"bad element id '{p[4]}'")
                : _sensor.PullGearToElem(srcOid, datIdent, DecodeSrc(p, 5))
                || Stop(directive, "drag item to element failed");
        }

        if (mark == "item")
        {
            return !TryDecodeUInt(p[4], out uint markOid)
                ? Stop(directive, $"bad target item guid '{p[4]}'")
                : _sensor.PullGearToGear(srcOid, markOid, DecodeSrc(p, 5))
                || Stop(directive, "drag item to item failed");
        }

        if (mark == "outside")
        {
            if (p.Length < 6) return Stop(directive, "usage: drag item <guid> outside <x> <y> [source]");
            if (!TryDecodeInt(p[4], out int x)) return Stop(directive, $"bad x coordinate '{p[4]}'");
            return !TryDecodeInt(p[5], out int y)
                ? Stop(directive, $"bad y coordinate '{p[5]}'")
                : _sensor.PullGearBeyond(srcOid, x, y, DecodeSrc(p, 6))
                || Stop(directive, "drag item outside failed");
        }

        return Stop(directive, "usage: drag item <guid> element/item/outside ...");
    }

    private bool DoPause(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length < 2) return Stop(directive, "usage: wait item|element|ms|world-ready|world-visible|materialized|render-pack|render-pack-samples|framebuffer|signal ...");

        string mark = p[1].ToLowerInvariant();
        if (mark == "item")
        {
            if (p.Length < 3 || !TryDecodeUInt(p[2], out uint gearOid)) return Stop(directive, "usage: wait item <guid> [source] [timeoutMs]");
            return _sensor.SeekByGearIdent(gearOid, DecodeSrc(p, 3)) is not null
                ? true
                : PauseOrTimeout(directive, TimeoutMsec(p, 3, 10000), $"item 0x{gearOid:X8}");
        }

        if (mark == "element")
        {
            if (p.Length < 3 || !TryDecodeUInt(p[2], out uint datIdent)) return Stop(directive, "usage: wait element <datId> [timeoutMs]");
            return _sensor.SeekByDatElemIdent(datIdent) is not null ? true : PauseOrTimeout(directive, TimeoutMsec(p, 3, 10000), $"element 0x{datIdent:X8}");
        }

        if (mark == "ms")
            return DoSleep(directive);

        if (mark == "world-ready")
        {
            if (_runtime is null) return Stop(directive, "world lifecycle automation is unavailable");
            return _runtime.IsRealmReady ? true : PauseOrTimeout(directive, TimeoutMsec(p, 2, 60000), "world readiness");
        }

        if (mark == "world-visible")
        {
            if (_runtime is null) return Stop(directive, "world lifecycle automation is unavailable");
            return _runtime.IsRealmViewRectShown ? true : PauseOrTimeout(directive, TimeoutMsec(p, 2, 60000), "normal world viewport");
        }

        if (mark == "materialized")
        {
            if (_runtime is null) return Stop(directive, "world lifecycle automation is unavailable");
            if (p.Length < 3 || !TryDecodeInt(p[2], out int occurrence) || occurrence <= 0)
                return Stop(directive, "usage: wait materialized <occurrence> [timeoutMs]");
            return _runtime.GatewayMaterializationCount >= occurrence
                ? true
                : PauseOrTimeout(directive, TimeoutMsec(p, 3, 60000), $"portal materialization {occurrence}");
        }

        if (mark == "render-pack-samples")
        {
            if (_runtime is null)
                return Stop(directive, "render-pack performance automation is unavailable");
            if (p.Length < 3
                || !TryDecodeInt(p[2], out int needed)
                || needed <= 0)
            {
                return Stop(
                    directive,
                    "usage: wait render-pack-samples <count> [timeoutMs]");
            }
            if (_runtime.RasterizeBundlePerformanceSpecimenTally >= needed)
                return true;
            return _runtime.RasterizeBundleFailedToCanon
                ? true
                : PauseOrTimeout(
                directive,
                TimeoutMsec(p, 3, 300000),
                $"{needed} render-pack performance samples");
        }

        if (mark == "render-pack")
        {
            if (_runtime is null)
                return Stop(directive, "render-pack selection automation is unavailable");
            if (p.Length < 3 || !TryNormalizeRasterizeBundlePreset(p[2], out string preset))
            {
                return Stop(
                    directive,
                    "usage: wait render-pack retail|low|medium|high|auto [timeoutMs]");
            }

            var condition = _runtime.RasterizeBundleCondition;
            bool expectCanon = string.Equals(
                preset,
                "retail",
                StringComparison.Ordinal);
            if (expectCanon
                && condition.State == CanonWidgetAutopilotRenderPackPhase.Retail
                && string.Equals(condition.PackId, "retail", StringComparison.OrdinalIgnoreCase)
                && string.Equals(condition.PresetId, "off", StringComparison.OrdinalIgnoreCase))

                return true;
            if (!expectCanon
                && condition.State == CanonWidgetAutopilotRenderPackPhase.Active
                && string.Equals(
                    condition.PackId,
                    "macac.atmospheric",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(condition.PresetId, preset, StringComparison.OrdinalIgnoreCase))

                return true;
            return condition.State == CanonWidgetAutopilotRenderPackPhase.FailedToRetail
                ? Stop(
                    directive,
                    $"render pack '{preset}' failed to retail: "
                    + (condition.FailureReason ?? "no failure reason was published"))
                : PauseOrTimeout(
                directive,
                TimeoutMsec(p, 3, 90000),
                $"render pack '{preset}' activation");
        }

        if (mark == "framebuffer")
        {
            if (_runtime is null)
                return Stop(directive, "framebuffer resize automation is unavailable");
            if (p.Length < 4
                || !TryDecodeInt(p[2], out int width)
                || !TryDecodeInt(p[3], out int height)
                || width <= 0
                || height <= 0)
            {
                return Stop(
                    directive,
                    "usage: wait framebuffer <width> <height> [timeoutMs]");
            }
            return _runtime.FramebufferWidth == width
                && _runtime.FramebufferHeight == height
                ? true
                : PauseOrTimeout(
                directive,
                TimeoutMsec(p, 4, 30000),
                $"framebuffer {width}x{height}");
        }

        if (mark == "signal")
        {
            if (_runtime is null)
                return Stop(directive, "automation signal runtime is unavailable");
            if (p.Length < 3)
                return Stop(directive, "usage: wait signal <name> [timeoutMs]");
            if (!_runtime.TryIsAutomationSignalPublished(
                    p[2],
                    out bool published,
                    out string problem))

                return Stop(directive, problem);
            return published
                ? true
                : PauseOrTimeout(
                directive,
                TimeoutMsec(p, 3, 120000),
                $"automation signal '{p[2]}'");
        }

        return Stop(directive, "usage: wait item|element|ms|world-ready|world-visible|materialized|render-pack|render-pack-samples|framebuffer|signal ...");
    }

    private bool DoRasterizeBundle(ScriptDirective directive)
    {
        if (_runtime is null)
            return Stop(directive, "render-pack automation is unavailable");
        string[] p = directive.Parts;
        if (p.Length is 2
            && string.Equals(p[1], "reset-performance", StringComparison.OrdinalIgnoreCase))
        {
            return _runtime.TryRestartRasterizeBundlePerformance(out string problem)
                || Stop(directive, problem);
        }
        if (p.Length is 3
            && string.Equals(p[1], "select", StringComparison.OrdinalIgnoreCase)
            && TryNormalizeRasterizeBundlePreset(p[2], out string preset))
        {
            return _runtime.TryPickRasterizeBundle(preset, out string problem)
                || Stop(directive, problem);
        }
        if (p.Length is 2
            && string.Equals(p[1], "disable", StringComparison.OrdinalIgnoreCase))
        {
            return _runtime.TryDeactivateRasterizeBundle(out string problem)
                || Stop(directive, problem);
        }
        return p.Length is 2
            && string.Equals(p[1], "reenable", StringComparison.OrdinalIgnoreCase)
            ? _runtime.TryReenableRasterizeBundle(out string reenableProblem)
                || Stop(directive, reenableProblem)
            : Stop(
            directive,
            "usage: renderpack reset-performance | renderpack select retail|low|medium|high|auto | renderpack disable | renderpack reenable");
    }

    private bool DoRescale(ScriptDirective directive)
    {
        if (_runtime is null)
            return Stop(directive, "framebuffer resize automation is unavailable");
        string[] p = directive.Parts;
        return p.Length is not 3
            || !TryDecodeInt(p[1], out int width)
            || !TryDecodeInt(p[2], out int height)
            || width <= 0
            || height <= 0
            ? Stop(directive, "usage: resize <width> <height>")
            : _runtime.TryRescaleFramebuffer(width, height, out string problem)
            || Stop(directive, problem);
    }

    private bool DoSleep(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        string val = p.Length >= 3 && string.Equals(p[0], "wait", StringComparison.OrdinalIgnoreCase) ? p[2]
            : p.Length >= 2 ? p[1] : "";
        return !TryDecodeInt(val, out int msec) || msec < 0
            ? Stop(directive, "usage: sleep <milliseconds> | wait ms <milliseconds>")
            : HasPassed(msec);
    }

    private bool DoInsist(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length < 5 || !string.Equals(p[1], "item", StringComparison.OrdinalIgnoreCase))
            return Stop(directive, "usage: assert item <guid> equip|container|slot <value>");
        if (!TryDecodeUInt(p[2], out uint gearOid)) return Stop(directive, $"bad item guid '{p[2]}'");

        string sort = p[3].ToLowerInvariant();
        CanonWidgetProbeAssertion outcome;
        if (sort == "equip")
        {
            if (!TryDecodeUInt(p[4], out uint bitmask)) return Stop(directive, $"bad equip mask '{p[4]}'");
            outcome = _sensor.InsistGear(gearOid, equippedLocale: (WieldBitmask)bitmask);
        }
        else if (sort == "container")
        {
            if (!TryDecodeUInt(p[4], out uint vesselIdent)) return Stop(directive, $"bad container id '{p[4]}'");
            outcome = _sensor.InsistGear(gearOid, vesselIdent: vesselIdent);
        }
        else if (sort == "slot")
        {
            if (!TryDecodeInt(p[4], out int socket)) return Stop(directive, $"bad slot '{p[4]}'");
            outcome = _sensor.InsistGear(gearOid, socket: socket);
        }
        else
        {
            return Stop(directive, "usage: assert item <guid> equip|container|slot <value>");
        }

        return outcome.Success || Stop(directive, outcome.Message);
    }

    private bool DoDirective(ScriptDirective directive)
    {
        string phrase = directive.Text[directive.Parts[0].Length..].Trim();
        if (phrase.Length is 0)
            return Stop(directive, "usage: command <chat-or-client-command>");
        if (_submitDirective is null)
            return Stop(directive, "command submission is unavailable");

        _submitDirective(phrase);
        return true;
    }

    private bool DoFeed(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length is not 3)
            return Stop(directive, "usage: input press|down|up <FeedAct>");
        if (!Enum.TryParse(p[2], ignoreCase: true, out FeedAct act)
            || act == FeedAct.None
            || !Enum.IsDefined(act))
            return Stop(directive, $"unknown input action '{p[2]}'");

        switch (p[1].ToLowerInvariant())
        {
            case "press":
                if (_pressFeed is null)
                    return Stop(directive, "input press injection is unavailable");
                return _pressFeed(act)
                    || Stop(directive, $"input press {act} was captured");

            case "down":
                if (_setFeedPinned is null)
                    return Stop(directive, "input held injection is unavailable");
                if (_pinnedFeeds.Contains(act))
                    return Stop(directive, $"input {act} is already down");
                if (!_setFeedPinned(act, true))
                    return Stop(directive, $"input down {act} was captured");
                _pinnedFeeds.Add(act);
                return true;

            case "up":
                if (_setFeedPinned is null)
                    return Stop(directive, "input held injection is unavailable");
                if (!_pinnedFeeds.Contains(act))
                    return Stop(directive, $"input {act} is not down");
                if (!_setFeedPinned(act, false))
                    return Stop(directive, $"input up {act} failed");
                _pinnedFeeds.Remove(act);
                return true;

            default:
                return Stop(directive, "usage: input press|down|up <FeedAct>");
        }
    }

    private bool DoPointerGaze(ScriptDirective directive)
    {
        string[] p = directive.Parts;
        if (p.Length is not 3
            || !TryDecodeFloat(p[1], out float dx)
            || !TryDecodeFloat(p[2], out float dy))
            return Stop(directive, "usage: mouselook <dx> <dy>");
        if (_fifoPointerGazeDiff is null)
            return Stop(directive, "mouselook injection is unavailable");
        _fifoPointerGazeDiff(dx, dy);
        return true;
    }

    private bool DoCheckpoint(ScriptDirective directive)
    {
        if (directive.Parts.Length is not 2)
            return Stop(directive, "usage: checkpoint <name>");
        if (_runtime is null)
            return Stop(directive, "world lifecycle automation is unavailable");

        if (_checkpoint is null)
        {
            if (!_runtime.TryReqCheckpoint(
                    directive.Parts[1],
                    out ICanonWidgetAutopilotCheckpoint? checkpoint,
                    out string problem))

                return Stop(directive, problem);

            if (checkpoint is null)
            {
                return Stop(
                    directive,
                    "world lifecycle automation accepted a checkpoint without returning its acknowledgement");
            }

            _checkpoint = checkpoint;
            _checkpointDirectiveOrdinal = _ordinal;
        }

        if (_checkpointDirectiveOrdinal != _ordinal)
        {
            return Stop(
                directive,
                "checkpoint acknowledgement belongs to a different script command");
        }

        var condition = _checkpoint.Status;
        if (condition == CanonWidgetAutopilotCheckpointStatus.Pending)
            return false;

        string? terminalProblem = _checkpoint.Error;
        _checkpoint = null;
        _checkpointDirectiveOrdinal = -1;
        return condition == CanonWidgetAutopilotCheckpointStatus.Succeeded
            || Stop(
                directive,
                terminalProblem
                ?? $"checkpoint '{directive.Parts[1]}' ended with {condition}");
    }

    private bool DoScreenshot(ScriptDirective directive)
    {
        if (directive.Parts.Length is < 2 or > 3)
            return Stop(directive, "usage: screenshot <name> [timeoutMs]");
        if (_runtime is null)
            return Stop(directive, "world lifecycle automation is unavailable");

        string label = directive.Parts[1];
        if (!_runtime.TryReqScreenshot(label, out string problem))
            return Stop(directive, problem);
        return _runtime.IsScreenshotDone(label)
            ? true
            : PauseOrTimeout(directive, TimeoutMsec(directive.Parts, 2, 10000), $"screenshot '{label}'");
    }

    private bool DoShutClient(ScriptDirective directive)
    {
        if (directive.Parts.Length is not 1)
            return Stop(directive, "usage: close-client");
        return _runtime is null
            ? Stop(directive, "client-close automation is unavailable")
            : _runtime.TryReqClientShut(out string problem)
            || Stop(directive, problem);
    }
}
