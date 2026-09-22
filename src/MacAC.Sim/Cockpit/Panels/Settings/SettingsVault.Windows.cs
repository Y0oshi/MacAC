using System.Globalization;
using System.Text.Json.Nodes;
using MacAC.Cockpit.Settings;

namespace MacAC.Cockpit.Panels.Settings;

public sealed partial class SettingsVault
{

    public ToonPrefs LoadCharacter(string toonTag)
    {
        ArgumentNullException.ThrowIfNull(toonTag);
        if (!File.Exists(_trail))
            return ToonPrefs.Default;
        try
        {
            if (ScanTrunk()?["character"]?[toonTag] is not JsonObject toon)
                return ToonPrefs.Default;

            var settings = ToonPrefs.Default;
            return new ToonPrefs(
                DefaultChatChannel: toon["defaultChatChannel"]?.GetValue<string>() ?? settings.DefaultChatChannel,
                AutoAttack: toon["autoAttack"]?.GetValue<bool>() ?? settings.AutoAttack,
                ConfirmSalvage: toon["confirmSalvage"]?.GetValue<bool>() ?? settings.ConfirmSalvage,
                ShowPickupMessages: toon["showPickupMessages"]?.GetValue<bool>() ?? settings.ShowPickupMessages);
        }
        catch (Exception exc)
        {
            Console.WriteLine($"settings: could not load {_trail}: {exc.Message} - using defaults");
            return ToonPrefs.Default;
        }
    }

    public void PersistToon(string toonTag, ToonPrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(toonTag);
        ArgumentNullException.ThrowIfNull(prefs);

        Correct(trunk => Branch(trunk, "character")[toonTag] = new JsonObject
        {
            ["autoAttack"] = prefs.AutoAttack,
            ["confirmSalvage"] = prefs.ConfirmSalvage,
            ["defaultChatChannel"] = prefs.DefaultChatChannel,
            ["showPickupMessages"] = prefs.ShowPickupMessages,
        });
    }

    public UiWindowOffset? PullPaneLocus(string toonTag, string paneLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);
        if (!File.Exists(_trail))
            return null;
        try
        {
            if (ScanTrunk()?["windowPositions"]?[toonTag]?[paneLabel] is not JsonObject joint)
                return null;
            float? x = joint["x"]?.GetValue<float>();
            float? y = joint["y"]?.GetValue<float>();
            return x is not null && y is not null ? new UiWindowOffset(x.Value, y.Value) : null;
        }
        catch (Exception exc)
        {
            Console.WriteLine($"settings: could not load window position from {_trail}: {exc.Message}");
            return null;
        }
    }

    /// <summary>Stores <c>windowPositions[toonKey][windowName]</c>, keeping every other section.</summary>
    public void PersistPaneLocus(string toonTag, string paneLabel, UiWindowOffset locus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);

        Correct(trunk => Branch(Branch(trunk, "windowPositions"), toonTag)[paneLabel] = new JsonObject
        {
            ["x"] = locus.X,
            ["y"] = locus.Y,
        });
    }

    public UiWindowArrangement? PullPaneArrangement(string toonTag, string resolutionTag, string paneLabel, UiWindowArrangement backup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);
        if (!File.Exists(_trail))
            return null;

        try
        {
            JsonObject? trunk = ScanTrunk();
            JsonObject? joint = trunk?["windowLayouts"]?[toonTag]?[resolutionTag]?[paneLabel] as JsonObject;
            if (joint is null
                && trunk?["windowLayouts"]?[toonTag] is JsonObject resolutions
                && Resolution.TryDecode(resolutionTag, out int wantWidth, out int wantHeight))
            {
                joint = Nearest(resolutions, paneLabel, wantWidth, wantHeight).Node;
            }

            if (joint is not null)
                return Arrangement.Read(joint, backup);

            // Schema 1 kept only X/Y and had no resolution key.
            return trunk?["windowPositions"]?[toonTag]?[paneLabel] is JsonObject legacy
                ? backup with { X = CockpitNode.Float(legacy, "x", backup.X), Y = CockpitNode.Float(legacy, "y", backup.Y) }
                : null;
        }
        catch (Exception exc)
        {
            Console.WriteLine($"settings: could not load window layout from {_trail}: {exc.Message}");
            return null;
        }
    }

    public void PersistPaneArrangement(string toonTag, string resolutionTag, string paneLabel, UiWindowArrangement arrangement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);

        Correct(trunk =>
            Branch(Branch(Branch(trunk, "windowLayouts"), toonTag), resolutionTag)[paneLabel] = Arrangement.Write(arrangement));
    }

    public UiWindowSpot? PullPaneStance(string toonTag, string resolutionTag, string paneLabel, UiWindowArrangement backup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);
        if (!Resolution.TryDecode(resolutionTag, out int wantWidth, out int wantHeight) || !File.Exists(_trail))
            return null;
        try
        {
            JsonObject? trunk = ScanTrunk();

            // Newest form first: the placement remembers its own screen size
            if ((trunk?["windowPlacements"] as JsonObject)?[toonTag] is JsonObject stances
                && stances[paneLabel] is JsonObject current
                && CockpitNode.Int(current, "screenWidth", 0) is > 0 and var width
                && CockpitNode.Int(current, "screenHeight", 0) is > 0 and var height
                && current["layout"] is JsonObject arrangement)
            {
                return new UiWindowSpot(Arrangement.Read(arrangement, backup), width, height);
            }

            // Then the closest per-resolution layout
            if ((trunk?["windowLayouts"] as JsonObject)?[toonTag] is JsonObject resolutions
                && Nearest(resolutions, paneLabel, wantWidth, wantHeight) is { Node: { } chosen } strike)
            {
                return new UiWindowSpot(Arrangement.Read(chosen, backup), strike.Width, strike.Height);
            }

            // Finally the schema-1 position, taken as if saved on this screen.
            if ((trunk?["windowPositions"] as JsonObject)?[toonTag] is JsonObject loci
                && loci[paneLabel] is JsonObject locus)
            {
                return new UiWindowSpot(
                    backup with { X = CockpitNode.Float(locus, "x", backup.X), Y = CockpitNode.Float(locus, "y", backup.Y) },
                    wantWidth,
                    wantHeight);
            }
            return null;
        }
        catch (Exception exc)
        {
            Console.WriteLine($"settings: could not load window placement from {_trail}: {exc.Message}");
            return null;
        }
    }

    public void PersistPaneStance(string toonTag, string paneLabel, UiWindowSpot stance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stance.ScreenWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stance.ScreenHeight);

        Correct(trunk =>
        {
            Branch(Branch(trunk, "windowPlacements"), toonTag)[paneLabel] = new JsonObject
            {
                ["screenWidth"] = stance.ScreenWidth,
                ["screenHeight"] = stance.ScreenHeight,
                ["layout"] = Arrangement.Write(stance.Layout),
            };
            string resolutionTag = Resolution.Key(stance.ScreenWidth, stance.ScreenHeight);
            Branch(Branch(Branch(trunk, "windowLayouts"), toonTag), resolutionTag)[paneLabel] = Arrangement.Write(stance.Layout);
        });
    }

    public UiWindowArrangement? PullNamedPaneArrangement(string profileLabel, string paneLabel, UiWindowArrangement backup)
    {
        ArgumentNullException.ThrowIfNull(profileLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);
        if (!File.Exists(_trail))
            return null;

        try
        {
            return ScanTrunk()?["namedWindowLayouts"]?[profileLabel]?[paneLabel] is JsonObject joint
                ? Arrangement.Read(joint, backup)
                : null;
        }
        catch (Exception exc)
        {
            Console.WriteLine($"settings: could not load named window layout from {_trail}: {exc.Message}");
            return null;
        }
    }

    public void PersistNamedPaneArrangement(string profileLabel, string paneLabel, UiWindowArrangement arrangement)
    {
        ArgumentNullException.ThrowIfNull(profileLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);

        Correct(trunk => Branch(Branch(trunk, "namedWindowLayouts"), profileLabel)[paneLabel] = Arrangement.Write(arrangement));
    }

    private static (JsonObject? Node, int Width, int Height) Nearest(JsonObject resolutions, string paneLabel, int wantWidth, int wantHeight)
    {
        (JsonObject? Node, int Width, int Height) finest = (null, wantWidth, wantHeight);
        long finestGap = long.MaxValue;
        foreach ((string tag, JsonNode? val) in resolutions)
        {
            if (val is not JsonObject panes || panes[paneLabel] is not JsonObject contender || !Resolution.TryDecode(tag, out int width, out int height))
                continue;
            long dx = (long)width - wantWidth;
            long dy = (long)height - wantHeight;
            long gap = dx * dx + dy * dy;
            if (gap >= finestGap)
                continue;
            finestGap = gap;
            finest = (contender, width, height);
        }
        return finest;
    }

    // The "WxH" key a layout is filed under
    private static class Resolution
    {
        public static string Key(int width, int height) =>
            string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");

        public static bool TryDecode(string tag, out int width, out int height)
        {
            width = 0;
            height = 0;
            int x = tag.IndexOf('x', StringComparison.OrdinalIgnoreCase);
            return x > 0
                && int.TryParse(tag.AsSpan(0, x), NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
                && int.TryParse(tag.AsSpan(x + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
                && width > 0
                && height > 0;
        }
    }

    // The on-disk shape of a UiWindowArrangement
    private static class Arrangement
    {
        public static UiWindowArrangement Read(JsonObject joint, UiWindowArrangement backup)
        {
            return new(
            CockpitNode.Float(joint, "x", backup.X),
            CockpitNode.Float(joint, "y", backup.Y),
            CockpitNode.Float(joint, "width", backup.Width),
            CockpitNode.Float(joint, "height", backup.Height),
            CockpitNode.Bool(joint, "visible", backup.Visible),
            CockpitNode.Bool(joint, "collapsed", backup.Collapsed),
            CockpitNode.Bool(joint, "maximized", backup.Maximized),
            CockpitNode.Int(joint, "authoredGeometryRevision", 0));
        }

        public static JsonObject Write(UiWindowArrangement arrangement)
        {
            return new()
            {
                ["x"] = arrangement.X,
                ["y"] = arrangement.Y,
                ["width"] = arrangement.Width,
                ["height"] = arrangement.Height,
                ["visible"] = arrangement.Visible,
                ["collapsed"] = arrangement.Collapsed,
                ["maximized"] = arrangement.Maximized,
                ["authoredGeometryRevision"] = arrangement.AuthoredGeometryRevision,
            };
        }
    }
}
