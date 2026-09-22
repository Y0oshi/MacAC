namespace MacAC.Mechanics.Gear;

public sealed class ClientThing
{
    private uint? _houseHolderIdent;
    private uint? _monarchIdent;
    private HouseAccessRecord? _access;

    // Raised when anything that decides house access changes
    internal event Action<ClientThing>? RestrictionAuthorityChanged;

    public uint ObjectId { get; init; }

    public uint WeenieClassIdent { get; set; }

    public string Name { get; set; } = "";

    public string PluralName { get; set; } = "";

    public GearKind Type { get; set; }

    public WieldBitmask ValidLocations { get; set; }

    public WieldBitmask CurrentlyEquippedLocale { get; set; }

    public uint IconId { get; set; }

    public uint GlyphUnderlayIdent { get; set; }

    public uint GlyphTopLayerIdent { get; set; }

    public uint Effects { get; set; }

    public int StackSize { get; set; } = 1;

    public int PileDimsUpper { get; set; } = 1;

    /// <summary>Total for the whole stack.</summary>
    public int Burden { get; set; }

    public int Value { get; set; }

    public uint VesselTag { get; set; }

    public int VesselSlot { get; set; } = -1;

    public uint VesselKindHint { get; set; }

    public bool Attuned { get; set; }

    public bool Bonded { get; set; }

    /// <summary>Zero when not wielded.</summary>
    public uint WielderIdent { get; set; }

    public int ItemsCapacity { get; set; }

    public int ContainersCapacity { get; set; }

    public uint HookItemTypes { get; set; }

    public uint HookType { get; set; }

    public bool IsTap => HookType is not 0u && HookItemTypes is not 0u;

    public uint Priority { get; set; }

    public uint? Useability { get; set; }

    public uint? TargetType { get; set; }

    public uint? PublicWeenieBitfield { get; set; }

    public uint PetHolderIdent { get; set; }

    public byte? CombatUse { get; set; }

    public ushort? AmmoType { get; set; }

    public uint? SpellId { get; set; }

    public IReadOnlyList<uint> AppraisedArcanumIds { get; internal set; } = [];

    public int PreviousAppraisalMomentMsec { get; internal set; }

    public uint? CooldownId { get; set; }

    public double? CooldownDuration { get; set; }

    public int BarterPhase { get; set; }

    public bool IsModulePack { get; set; }

    public byte? RadarBlipColor { get; set; }

    public byte? RadarBehavior { get; set; }

    public int Structure { get; set; }

    public int MaxStructure { get; set; }

    /// <summary>0..10, fractional on the wire.</summary>
    public float Workmanship { get; set; }

    public uint? MaterialType { get; set; }

    public TraitBundle Properties { get; } = new();

    public uint? HouseHolderIdent
    {
        get => _houseHolderIdent;
        set
        {
            if (_houseHolderIdent == value)
                return;
            _houseHolderIdent = value;
            RestrictionAuthorityChanged?.Invoke(this);
        }
    }

    public uint? MonarchIdent
    {
        get => _monarchIdent;
        set
        {
            if (_monarchIdent == value)
                return;
            _monarchIdent = value;
            RestrictionAuthorityChanged?.Invoke(this);
        }
    }

    public HouseAccessRecord? Restrictions
    {
        get => _access;
        set
        {
            if (ReferenceEquals(_access, value))
                return;
            _access = value;
            RestrictionAuthorityChanged?.Invoke(this);
        }
    }

    /// <summary>The plural for stacks, falling back to the English -s/-es rule.</summary>
    public string FetchAppropriateLabel()
    {
        if (StackSize <= 1 || string.IsNullOrEmpty(Name))
            return Name;
        if (!string.IsNullOrEmpty(PluralName))
            return PluralName;
        return Name.EndsWith('s') ? Name + "es" : Name + "s";
    }

    public string FetchHintReadoutLabel()
    {
        string label = FetchAppropriateLabel();
        return StackSize > 1 ? $"{StackSize} {label}" : label;
    }
}

/// <summary>A parsed PublicWeenieDesc, before it is applied to a <see cref="ClientThing"/>.</summary>
public readonly record struct WeenieRecord(
    uint Guid,
    string? Name,
    GearKind? Type,
    uint WeenieClassId,
    uint IconId,
    uint IconOverlayId,
    uint IconUnderlayId,
    uint Effects,
    int? Value,
    int? StackSize,
    int? StackSizeMax,
    int? Burden,
    uint? ContainerId,
    uint? WielderId,
    uint? ValidLocations,
    uint? CurrentWieldedLocation,
    uint? Priority,
    int? ItemsCapacity,
    int? ContainersCapacity,
    int? Structure,
    int? MaxStructure,
    float? Workmanship,
    uint? Useability = null,
    uint? TargetType = null,
    byte? RadarBlipColor = null,
    byte? RadarBehavior = null,
    uint? PublicWeenieBitfield = null,
    byte? CombatUse = null,
    string? PluralName = null,
    uint? PetOwnerId = null,
    ushort? AmmoType = null,
    uint? SpellId = null,
    uint? CooldownId = null,
    double? CooldownDuration = null,
    uint? HookItemTypes = null,
    uint? HookType = null,
    uint? MaterialType = null,
    uint? HouseOwnerId = null,
    uint? MonarchId = null,
    HouseAccessRecord? Restrictions = null);
