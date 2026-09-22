using MacAC.Dat;
using MacAC.Mechanics.Data;

namespace MacAC.Mechanics.Controls;

public enum CanonActionClass : uint
{
    None = 0,
    Movement = 1,
    Camera = 2,
    Ui = 3,
    Combat = 4,
    Emote = 5,
    // 6 really is absent from the shipped 2013 DAT; this is not a gap in the table.
    CharacterSettings = 7,
}

public static class CanonActionMapIds
{
    public const uint ActLookupIdent = 0x26000000u;

    public const uint GameplayMasterLookupIdent = 0x14000000u;

    public const uint SysMasterLookupIdent = 0x14000002u;
}

public readonly record struct CanonKeyChord(uint Scan, uint Device, uint Modifier, uint Activation);

public sealed record CanonActionMapRow(
    uint InputMapId,
    uint ActionId,
    CanonActionClass ActionClass,
    uint LabelHash,
    uint TooltipHash,
    IReadOnlyList<CanonKeyChord> DefaultBindings);

/// <summary>Every bindable action the DAT declares, with its default chords.</summary>
public sealed record CanonActionMapFrame(
    IReadOnlyList<CanonActionMapRow> Rows,
    IReadOnlyDictionary<uint, IReadOnlySet<uint>>? ConflictingInputMaps = null)
{
    public bool FeedLookupsConflict(uint leftFeedLookupIdent, uint rightFeedLookupIdent)
    {
        if (leftFeedLookupIdent == rightFeedLookupIdent)
            return true;
        return ConflictingInputMaps is not null
            && ConflictingInputMaps.TryGetValue(leftFeedLookupIdent, out IReadOnlySet<uint>? rivals)
            && rivals.Contains(rightFeedLookupIdent);
    }
}

public static class CanonInputMapHeaders
{
    public const uint StringChartIdent = 0x23000005u;

    public static readonly IReadOnlyDictionary<uint, string> LabelByFeedLookupIdent =
        new Dictionary<uint, string>
        {
            [0x00000004u] = "ID_InputMap_MovementCommands",
            [0x00000005u] = "ID_InputMap_CameraControls",
            [0x00000006u] = "ID_InputMap_CameraAlternateControls",
            [0x00000009u] = "ID_InputMap_DialogBoxes",
            [0x0000000Bu] = "ID_InputMap_DebugConsole",
            [0x0000000Cu] = "ID_InputMap_ProfilerUI",
            [0x0000000Du] = "ID_InputMap_UIDebugger",
            [0x0000000Eu] = "ID_InputMap_DebugCommands",
            [0x10000002u] = "ID_InputMap_Combat",
            [0x10000003u] = "ID_InputMap_MeleeCombat",
            [0x10000004u] = "ID_InputMap_MissileCombat",
            [0x10000005u] = "ID_InputMap_MagicCombat",
            [0x10000006u] = "ID_InputMap_Emotes",
            [0x10000007u] = "ID_InputMap_ItemSelectionCommands",
            [0x10000008u] = "ID_InputMap_CharacterOptionCommands",
            [0x10000009u] = "ID_InputMap_UICommands",
            [0x1000000Au] = "ID_InputMap_ChatCommands",
            [0x1000000Cu] = "ID_InputMap_QuickslotCommands",
            [0x1000000Du] = "ID_InputMap_ToggleChatEntry",
        };
}

public static class CanonActionMapReader
{
    public static CanonActionMapFrame? Read(IDatRecordSource datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);

        if (datFiles.Get<ActionMap>(CanonActionMapIds.ActLookupIdent) is not { } actLookup)
            return null;

        MasterInputMap?[] masters =
        [
            datFiles.Get<MasterInputMap>(CanonActionMapIds.GameplayMasterLookupIdent),
            datFiles.Get<MasterInputMap>(CanonActionMapIds.SysMasterLookupIdent),
        ];

        var ranks = new List<CanonActionMapRow>();
        foreach ((uint feedLookupIdent, var acts) in actLookup.InputMaps)
        {
            foreach ((uint actIdent, ActionBinding act) in acts)
            {
                UserBinding mapping = act.Binding;
                uint classIdent = mapping?.ActionClass ?? 0u;
                if (classIdent is 0u)
                    continue;

                List<CanonKeyChord> chords = new List<CanonKeyChord>();
                foreach (MasterInputMap? master in masters)
                    CollectChords(master, feedLookupIdent, actIdent, chords);

                ranks.Add(new CanonActionMapRow(
                    feedLookupIdent,
                    actIdent,
                    (CanonActionClass)classIdent,
                    mapping!.ActionName,
                    mapping.ActionDescription,
                    chords));
            }
        }

        var rivals = new Dictionary<uint, IReadOnlySet<uint>>();
        foreach ((uint tag, InputConflicts val) in actLookup.Conflicts)
        {
            uint holder = val.InputMap is not 0u ? val.InputMap : tag;
            rivals[holder] = new HashSet<uint>(val.ConflictingInputMaps);
        }

        return new CanonActionMapFrame(ranks, rivals);
    }

    private static void CollectChords(
        MasterInputMap? master,
        uint feedLookupIdent,
        uint actIdent,
        List<CanonKeyChord> into)
    {
        if (master is null || !master.InputMaps.TryGetValue(feedLookupIdent, out var feedLookup))
            return;

        foreach (QualifiedControl control in feedLookup.Mappings)
        {
            if (control.Unknown != actIdent)
                continue;
            uint dense = control.Key.Key;
            into.Add(new CanonKeyChord(
                Scan: (dense >> 16) & 0xFFFFu,
                Device: dense & 0xFFFFu,
                Modifier: control.Key.Modifier,
                Activation: control.Activation));
        }
    }
}
