using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MacAC.Cockpit.Input;

namespace MacAC.Client.Controls;

public static partial class CanonKeymapFile
{
    private static readonly Regex MappingStroke = new(
        "^(?<action>[A-Za-z0-9_]+)\\s*\\[\\s*\"\"\\s*\\[\\s*"
        + "(?<device>[0-9]+)\\s+(?<control>[A-Za-z0-9_]+)"
        + "(?:\\s+(?<sub>[A-Za-z]+))?\\s*\\]"
        + "(?:\\s+(?<modifier>0x[0-9A-Fa-f]+|[0-9]+))?"
        + "(?:\\s+(?<activation>[A-Za-z]+))?\\s*\\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (uint Id, string Name)[] ClusterOrdering =
    [
        (0x00000004u, "MovementCommands"),
        (0x10000007u, "ItemSelectionCommands"),
        (0x10000009u, "UICommands"),
        (0x1000000Cu, "QuickslotCommands"),
        (0x1000000Du, "ToggleChatEntry"),
        (0x1000000Au, "ChatCommands"),
        (0x10000002u, "Combat"),
        (0x10000003u, "MeleeCombat"),
        (0x10000004u, "MissileCombat"),
        (0x10000005u, "MagicCombat"),
        (0x10000006u, "Emotes"),
        (0x00000005u, "CameraControls"),
        (0x00000006u, "CameraAlternateControls"),
        (0x10000008u, "CharacterOptionCommands"),
    ];

    private static readonly IReadOnlyDictionary<string, uint> ClusterIdents =
        ClusterOrdering.ToDictionary(static cluster => cluster.Name, static cluster => cluster.Id,
            StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<IReadOnlyDictionary<FeedAct, string>> ActLabelsHolder =
        new(AssembleActLabels);

    private static readonly Lazy<IReadOnlyDictionary<string, FeedAct>> ActsByFileLabelHolder =
        new(AssembleActsByFileLabel);

    private static readonly IReadOnlyDictionary<FeedAct, string> ToonKnobLabels =
        new Dictionary<FeedAct, string>
        {
            [FeedAct.ToggleCharacterOptionAutoRepeatAttack] = "AutoRepeatAttacks",
            [FeedAct.ToggleCharacterOptionIgnoreAllegianceRequests] = "IgnoreAllegianceRequests",
            [FeedAct.ToggleCharacterOptionIgnoreFellowshipRequests] = "IgnoreFellowshipRequests",
            [FeedAct.ToggleCharacterOptionIgnoreTradeRequests] = "IgnoreTradeRequests",
            [FeedAct.ToggleCharacterOptionPersistentAtDay] = "PersistentAtDay",
            [FeedAct.ToggleCharacterOptionAllowGive] = "LetPlayersGiveYouItems",
            [FeedAct.ToggleCharacterOptionViewCombatTarget] = "AutoTrackCombatTargets",
            [FeedAct.ToggleCharacterOptionShowTooltips] = "DisplayTooltips",
            [FeedAct.ToggleCharacterOptionUseDeception] = "AttemptToDeceivePlayers",
            [FeedAct.ToggleCharacterOptionToggleRun] = "RunAsDefaultMovement",
            [FeedAct.ToggleCharacterOptionStayInChatMode] = "StayInChatModeAfterSend",
            [FeedAct.ToggleCharacterOptionAdvancedCombatUi] = "AdvancedCombatInterface",
            [FeedAct.ToggleCharacterOptionAutoTarget] = "AutoTarget",
            [FeedAct.ToggleCharacterOptionVividTargetingIndicator] = "VividTargetIndicator",
            [FeedAct.ToggleCharacterOptionFellowshipShareXp] = "ShareFellowshipXP",
            [FeedAct.ToggleCharacterOptionAcceptLootPermits] = "AcceptCorpseLooting",
            [FeedAct.ToggleCharacterOptionFellowshipShareLoot] = "ShareFellowshipLoot",
            [FeedAct.ToggleCharacterOptionFellowshipAutoAcceptRequests] = "AutomaticallyAcceptFellowshipRequests",
            [FeedAct.ToggleCharacterOptionCoordinatesOnRadar] = "ShowRadarCoordinates",
            [FeedAct.ToggleCharacterOptionSpellDuration] = "ShowSpellDurations",
            [FeedAct.ToggleCharacterOptionDisableHouseRestrictionEffects] = "DisableHouseEffect",
            [FeedAct.ToggleCharacterOptionDragItemOnPlayerOpensSecureTrade] = "DragItemOnPlayerOpensSecureTrade",
            [FeedAct.ToggleCharacterOptionDisplayAllegianceLogonNotifications] = "DisplayAllegianceLogonNotifications",
            [FeedAct.ToggleCharacterOptionUseChargeAttack] = "UseChargeAttack",
            [FeedAct.ToggleCharacterOptionUseCraftSuccessDialog] = "ToggleCraftingChanceOfSuccessDialog",
            [FeedAct.ToggleCharacterOptionListenToAllegianceChat] = "AllegianceChat",
            [FeedAct.ToggleCharacterOptionDisplayDateOfBirth] = "DisplayDateOfBirth",
            [FeedAct.ToggleCharacterOptionDisplayAge] = "DisplayAge",
            [FeedAct.ToggleCharacterOptionDisplayChessRank] = "DisplayChessRank",
            [FeedAct.ToggleCharacterOptionDisplayFishingSkill] = "Fishing",
            [FeedAct.ToggleCharacterOptionDisplayNumberDeaths] = "DisplayNumberDeaths",
            [FeedAct.ToggleCharacterOptionDisplayTimeStamps] = "DisplayTimeStamps",
            [FeedAct.ToggleCharacterOptionSalvageMultiple] = "SalvageMultiple",
            [FeedAct.ToggleCharacterOptionListenToGeneralChat] = "GeneralChat",
            [FeedAct.ToggleCharacterOptionListenToTradeChat] = "TradeChat",
            [FeedAct.ToggleCharacterOptionListenToLfgChat] = "LFGChat",
            [FeedAct.ToggleCharacterOptionListenToRoleplayChat] = "RoleplayChat",
            [FeedAct.ToggleCharacterOptionDisplayNumberCharacterTitles] = "DisplayNumberCharacterTitles",
            [FeedAct.ToggleCharacterOptionMainPackPreferred] = "MainPackPreferred",
            [FeedAct.ToggleCharacterOptionLeadMissileTargets] = "LeadMissileTargets",
            [FeedAct.ToggleCharacterOptionUseFastMissiles] = "UseFastMissiles",
            [FeedAct.ToggleCharacterOptionFilterLanguage] = "FilterLanguage",
            [FeedAct.ToggleCharacterOptionConfirmVolatileRareUse] = "ConfirmVolatileRareUse",
            [FeedAct.ToggleCharacterOptionListenToSocietyChat] = "SocietyChat",
            [FeedAct.ToggleCharacterOptionShowHelm] = "ShowHelm",
            [FeedAct.ToggleCharacterOptionDisableDistanceFog] = "DisableDistanceFog",
            [FeedAct.ToggleCharacterOptionShowCloak] = "ShowCloak",
            [FeedAct.ToggleCharacterOptionSideBySideVitals] = "SideBySideVitals",
        };

    private const string FixedCanonLookups = """
      TargetedUsage
      [
        SelectLeft [ "" [ 1 DIMOFS_BUTTON0 ] ]
        SelectRight [ "" [ 1 DIMOFS_BUTTON1 ] ]
      ]

      SystemKeys
      [
        AltEnter [ "" [ 0 DIK_RETURN ] 0x00000004 ]
        AltTab [ "" [ 0 DIK_TAB ] 0x00000004 ]
        AltF4 [ "" [ 0 DIK_F4 ] 0x00000004 ]
        CtrlShiftEsc [ "" [ 0 DIK_ESCAPE ] 0x00000003 ]
      ]

      MouseCommands
      [
        PointerX [ "" [ 1 DIMOFS_X ] 0x00000000 Analog ]
        PointerY [ "" [ 1 DIMOFS_Y ] 0x00000000 Analog ]
        SelectLeft [ "" [ 1 DIMOFS_BUTTON0 ] ]
        SelectRight [ "" [ 1 DIMOFS_BUTTON1 ] ]
        SelectMid [ "" [ 1 DIMOFS_BUTTON2 ] ]
        SelectDblLeft [ "" [ 1 DIMOFS_BUTTON0 ] 0x00000000 MouseDblClick ]
        SelectDblRight [ "" [ 1 DIMOFS_BUTTON1 ] 0x00000000 MouseDblClick ]
        SelectDblMid [ "" [ 1 DIMOFS_BUTTON2 ] 0x00000000 MouseDblClick ]
      ]

      ScrollableControls
      [
        ScrollUp [ "" [ 1 DIMOFS_Z AxisPositive ] ]
        ScrollDown [ "" [ 1 DIMOFS_Z AxisNegative ] ]
        ScrollUp [ "" [ 0 DIK_UPARROW ] 0x00000002 ]
        ScrollDown [ "" [ 0 DIK_DOWNARROW ] 0x00000002 ]
      ]

      EditControls
      [
        CursorCharLeft [ "" [ 0 DIK_LEFT ] ]
        CursorCharRight [ "" [ 0 DIK_RIGHTARROW ] ]
        CursorPreviousLine [ "" [ 0 DIK_UPARROW ] ]
        CursorNextLine [ "" [ 0 DIK_DOWNARROW ] ]
        CursorPreviousPage [ "" [ 0 DIK_PGUP ] ]
        CursorNextPage [ "" [ 0 DIK_PGDN ] ]
        CursorWordLeft [ "" [ 0 DIK_LEFT ] 0x00000002 ]
        CursorWordRight [ "" [ 0 DIK_RIGHTARROW ] 0x00000002 ]
        CursorStartOfLine [ "" [ 0 DIK_HOME ] ]
        CursorStartOfDocument [ "" [ 0 DIK_HOME ] 0x00000002 ]
        CursorEndOfLine [ "" [ 0 DIK_END ] ]
        CursorEndOfDocument [ "" [ 0 DIK_END ] 0x00000002 ]
        EscapeKey [ "" [ 0 DIK_ESCAPE ] ]
        AcceptInput [ "" [ 0 DIK_RETURN ] ]
        DeleteKey [ "" [ 0 DIK_DELETE ] ]
        BackspaceKey [ "" [ 0 DIK_BACK ] ]
      ]

      CopyAndPasteControls
      [
        CopyText [ "" [ 0 DIK_C ] 0x00000002 ]
        CopyText [ "" [ 0 DIK_INSERT ] 0x00000002 ]
        CutText [ "" [ 0 DIK_X ] 0x00000002 ]
        CutText [ "" [ 0 DIK_DELETE ] 0x00000001 ]
        PasteText [ "" [ 0 DIK_V ] 0x00000002 ]
        PasteText [ "" [ 0 DIK_INSERT ] 0x00000001 ]
      ]

      DialogBoxes
      [
        EscapeKey [ "" [ 0 DIK_ESCAPE ] ]
        AcceptInput [ "" [ 0 DIK_RETURN ] ]
      ]

    """;
}

public enum CanonKeymapSaveStatus
{
    Saved,
    Exists,
    ReadOnly,
    InvalidName,
    Failed,
}

public readonly record struct CanonKeymapSaveResult(
    CanonKeymapSaveStatus Status,
    string FileName,
    string? Error = null);

public sealed class CanonKeymapProfileStore
{
    public const string DefaultFileLabel = "macac.keymap";

    private readonly string _jsonTrail;
    private readonly string _selectorTrail;

    public CanonKeymapProfileStore(string jsonTrail, string? keymapFolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonTrail);
        _jsonTrail = Path.GetFullPath(jsonTrail);
        string settingsFolder = Path.GetDirectoryName(_jsonTrail)
            ?? Directory.GetCurrentDirectory();
        _selectorTrail = Path.Combine(settingsFolder, "active-keymap.txt");
        _directory = keymapFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Asheron's Call");
    }

    private readonly string _directory;

    public string FolderTrail => _directory;
    public string LatestFileLabel
    {
        get
        {
            try
            {
                if (File.Exists(_selectorTrail))
                {
                    string chosen = StandardizeFileLabel(File.ReadAllText(_selectorTrail));
                    if (chosen.Length is not 0) return chosen;
                }
            }
            catch (Exception miss)
            {
                Console.WriteLine($"keymap: active-profile preference could not be read: {miss.Message}");
            }
            return DefaultFileLabel;
        }
    }

    public IReadOnlyList<string> RosterFiles()
    {
        try
        {
            return !Directory.Exists(_directory)
                ? Array.Empty<string>()
                : [.. Directory.EnumerateFiles(_directory, "*.keymap", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(static label => !string.IsNullOrEmpty(label))
                .Cast<string>()
                .OrderBy(static label => label, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception miss)
        {
            Console.WriteLine($"keymap: profile list failed: {miss.Message}");
            return Array.Empty<string>();
        }
    }

    public bool TryPull(
        string fileLabel,
        KeyBindingBook baseMappings,
        out KeyBindingBook mappings,
        out string? problem)
    {
        mappings = baseMappings;
        problem = null;
        string normalized = StandardizeFileLabel(fileLabel);
        if (normalized.Length is 0)
        {
            problem = "The keymap filename is invalid.";
            return false;
        }

        try
        {
            string phrase = File.ReadAllText(Path.Combine(_directory, normalized));
            mappings = CanonKeymapFile.Parse(phrase, baseMappings);
            EmitSelector(normalized);
            return true;
        }
        catch (Exception miss)
        {
            problem = miss.Message;
            return false;
        }
    }

    public CanonKeymapSaveResult Save(
        string fileLabel,
        KeyBindingBook mappings,
        bool overwrite)
    {
        string normalized = StandardizeFileLabel(fileLabel);
        if (normalized.Length is 0)
            return new(CanonKeymapSaveStatus.InvalidName, string.Empty);

        string trail = Path.Combine(_directory, normalized);
        try
        {
            if (File.Exists(trail))
            {
                if (!overwrite)
                    return new(CanonKeymapSaveStatus.Exists, normalized);
                if ((File.GetAttributes(trail) & FileAttributes.ReadOnly) != 0)
                    return new(CanonKeymapSaveStatus.ReadOnly, normalized);
            }

            Directory.CreateDirectory(_directory);
            AtomicEmit(trail, CanonKeymapFile.Write(mappings));
            EmitSelector(normalized);
            return new(CanonKeymapSaveStatus.Saved, normalized);
        }
        catch (UnauthorizedAccessException miss)
        {
            return new(CanonKeymapSaveStatus.ReadOnly, normalized, miss.Message);
        }
        catch (Exception miss)
        {
            return new(CanonKeymapSaveStatus.Failed, normalized, miss.Message);
        }
    }

    public CanonKeymapSaveResult PersistEngaged(KeyBindingBook mappings) =>
        Save(LatestFileLabel, mappings, overwrite: true);

    public static KeyBindingBook PullEngagedOrJson(
        string jsonTrail,
        out string profileLabel)
    {
        KeyBindingBook backup = KeyBindingBook.PullOrDefault(jsonTrail);
        CanonKeymapProfileStore vault = new CanonKeymapProfileStore(jsonTrail);
        profileLabel = vault.LatestFileLabel;
        string profileTrail = Path.Combine(vault.FolderTrail, profileLabel);
        if (!File.Exists(profileTrail)) return backup;
        if (vault.TryPull(profileLabel, backup, out KeyBindingBook fetched, out string? problem))
            return fetched;
        Console.WriteLine($"keymap: '{profileLabel}' could not be loaded; using JSON/defaults: {problem}");
        return backup;
    }

    public static string StandardizeFileLabel(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return string.Empty;
        string trimmed = val.Trim();
        if (!string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal))
            return string.Empty;
        return trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? string.Empty
            : trimmed.EndsWith(".keymap", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".keymap";
    }

    private void EmitSelector(string fileLabel)
    {
        string? folder = Path.GetDirectoryName(_selectorTrail);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        AtomicEmit(_selectorTrail, fileLabel + Environment.NewLine);
    }

    private static void AtomicEmit(string trail, string substance)
    {
        string tmp = trail + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            File.WriteAllText(tmp, substance, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmp, trail, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
