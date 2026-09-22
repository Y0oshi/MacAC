using MacAC.Mechanics.Genesis;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class SimToonGenesisLedger : IDisposable
{
    private static readonly GenesisTraitId[] BalanceOrdering =
    [
        GenesisTraitId.Strength,
        GenesisTraitId.Endurance,
        GenesisTraitId.Coordination,
        GenesisTraitId.Quickness,
        GenesisTraitId.Focus,
        GenesisTraitId.Self,
    ];

    private sealed class Lens(SimToonGenesisLedger holder)
        : ISimToonGenesisLens
    {
        public SimToonGenesisCapture Snapshot => holder.Snapshot;

        public GenesisSkillTrack GetSkillLevel(uint aptitudeIdent) =>
            holder.GetSkillLevel(aptitudeIdent);

        public GenesisOptions Options => holder._knobs;

        public IDisposable Subscribe(ISimToonGenesisWatcher watcher) =>
            holder._signals.Subscribe(watcher);
    }

    private readonly object _latch = new();

    private readonly ToonGenesisEventFlow _signals = new();

    private readonly Lens _lens;

    private GenesisOptions _knobs;

    private readonly Random _random;

    private SimEpochTicket _epoch;

    private bool _engaged;

    private long _rev;

    private uint _lineage;

    private uint _gender;

    private SimToonGenesisAppearance _looks =
        SimToonGenesisAppearance.Default;

    private uint _blueprint = SimToonGenesisCapture.BlueprintUnset;

    private GenesisAttributeSpread _attrs;

    private uint _boltedTraits;

    private uint _traitCredits;

    private int _traitCreditsLeft;

    private uint _aptitudeCredits;

    private int _aptitudeCreditsLeft;

    private readonly GenesisSkillTrackSet _aptitudes = new();

    private int _balanceCur = 1;

    private string _label = string.Empty;

    private int _beginArea = -1;

    private uint _socket;

    private bool _expectingVerdict;

    private SimToonGenesisLocalRefusal _refusal;

    private SimToonGenesisRejection? _rejection;

    private SimToonGenesisIdentity? _built;

    private bool _destroyed;

    public SimToonGenesisLedger(
        GenesisOptions options,
        Random? random = null)
    {
        _knobs = options ?? throw new ArgumentNullException(nameof(options));
        _random = random ?? Random.Shared;
        _lens = new Lens(this);
    }

    public ISimToonGenesisLens View => _lens;

    public GenesisOptions Options => _knobs;

    public void SetupKnobs(GenesisOptions knobs)
    {
        ArgumentNullException.ThrowIfNull(knobs);
        lock (_latch)
        {
            Live();
            if (_engaged)
            {
                throw new InvalidOperationException(
                    "Chargen options can't be installed while a character-creation session is active");
            }
            _knobs = knobs;
        }
    }

    public SimToonGenesisCapture Snapshot
    {
        get
        {
            lock (_latch)
            {
                return new SimToonGenesisCapture(
                    _epoch,
                    _engaged,
                    _rev,
                    _lineage,
                    _gender,
                    _looks,
                    _blueprint,
                    _attrs,
                    _boltedTraits,
                    _traitCredits,
                    _traitCreditsLeft,
                    _aptitudeCredits,
                    _aptitudeCreditsLeft,
                    _label,
                    _beginArea,
                    _socket,
                    _expectingVerdict,
                    _refusal,
                    _rejection,
                    _built);
            }
        }
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            _engaged = false;
            Wipe();
            ++_rev;
        }
        _signals.Dispose();
    }

    internal void Begin(SimEpochTicket gen)
    {
        lock (_latch)
        {
            Live();
            _epoch = gen;
            _engaged = true;
            Wipe();
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.Reset);
    }

    internal void ConcludeJoin()
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return;
            _engaged = false;
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
    }

    internal void Reset(SimEpochTicket gen)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _epoch = gen;
            _engaged = false;
            Wipe();
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.Reset);
    }

    internal bool TryPickLineage(uint lineageIdent)
    {
        if (!_knobs.TryFetchLineage(lineageIdent, out GenesisHeritageOptions? lineage))
            return false;

        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return false;

            ChooseLineage(lineageIdent, lineage);
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryPickGender(uint genderTag)
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged || _lineage is 0)
                return false;
            if (!_knobs.TryFetchLineage(_lineage, out GenesisHeritageOptions? lineage)
                || !lineage.GendersByKey.ContainsKey((int)genderTag))

                return false;

            ChooseGender(genderTag);
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryPickBlueprint(uint blueprintOrdinal)
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged || _lineage is 0 || _gender is 0)
                return false;
            if (!_knobs.TryFetchLineage(_lineage, out GenesisHeritageOptions? lineage)
                || blueprintOrdinal >= (uint)lineage.Templates.Count)

                return false;

            _blueprint = blueprintOrdinal;
            ImposeBlueprint(lineage);
            RecountTraitCredits();
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryPickBeginArea(int ordinal)
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return false;
            if (ordinal < 0 || ordinal >= _knobs.StarterAreas.Count)
                return false;
            _beginArea = ordinal;
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TrySetLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        string bounded = label.Length > 32 ? label[..32] : label;
        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return false;
            _label = bounded;
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TrySetSocket(uint socket)
    {
        lock (_latch)
        {
            if (_destroyed || !_engaged)
                return false;
            _socket = socket;
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.StateChanged);
        return true;
    }

    internal bool TryCommenceComplete(
        int lineupTally,
        int socketTally,
        out CharacterForge.WireRequest req,
        out uint[] aptitudeAdvancementClasses,
        out SimToonGenesisLocalRefusal refusal,
        bool confirmedUnspentCredits = false)
    {
        req = default;
        aptitudeAdvancementClasses = [];
        bool approved;
        lock (_latch)
        {
            if (_destroyed || !_engaged)
            {
                refusal = SimToonGenesisLocalRefusal.None;
                return false;
            }

            string trimmed = _label.Trim();
            _label = trimmed;

            refusal = trimmed.Length is 0
                ? new SimToonGenesisLocalRefusal(
                    NoName: true, false, false, false)
                : _lineage is 0 || _gender is 0
                    ? new SimToonGenesisLocalRefusal(
                        false, false, false, false, HeritageOrGenderUnset: true)
                    : !confirmedUnspentCredits && _traitCreditsLeft > 0
                        ? new SimToonGenesisLocalRefusal(
                            false, AttributeCreditsUnspent: true, false, false)
                        : _expectingVerdict
                            ? new SimToonGenesisLocalRefusal(
                                false, false, AlreadyPending: true, false)
                            : socketTally > 0 && lineupTally >= socketTally
                                ? new SimToonGenesisLocalRefusal(
                                    false, false, false, RosterFull: true)
                                : SimToonGenesisLocalRefusal.None;

            _refusal = refusal;
            approved = !refusal.Any;
            if (approved)
            {
                _expectingVerdict = true;
                req = ConstructReq();
                aptitudeAdvancementClasses = AptitudeArr();
            }
            ++_rev;
        }
        Publish(approved
            ? SimToonGenesisDiffKind.FinishSent
            : SimToonGenesisDiffKind.FinishRefused);
        return approved;
    }

    internal void ImposeCreationResponse(GenesisVerdict.Parsed response)
    {
        SimToonGenesisDiffKind sort;
        lock (_latch)
        {
            if (_destroyed || !_engaged || !_expectingVerdict)
                return;

            _expectingVerdict = false;
            if (response.IsOk
                && response.Guid is { } oid
                && response.Name is { } label)
            {
                _built = new SimToonGenesisIdentity(oid, label);
                _rejection = null;
                sort = SimToonGenesisDiffKind.Created;
            }
            else
            {
                string cause = response.AsCode.ToString();
                _rejection = new SimToonGenesisRejection(
                    response.RawCode,
                    response.AsCode,
                    cause,
                    _label);
                sort = SimToonGenesisDiffKind.CreationFailed;
            }
            ++_rev;
        }
        Publish(sort);
    }

    internal bool TryAcknowledgeRejection()
    {
        lock (_latch)
        {
            if (_destroyed || _rejection is null)
                return false;
            _rejection = null;
            ++_rev;
        }
        Publish(SimToonGenesisDiffKind.RejectionAcknowledged);
        return true;
    }

    private void ChooseLineage(uint lineageIdent, GenesisHeritageOptions lineage)
    {
        _lineage = lineageIdent;
        _traitCredits = lineage.AttributeCredits;
        _aptitudeCredits = lineage.SkillCredits;
        _aptitudeCreditsLeft = checked((int)lineage.SkillCredits);

        ImposeBlueprint(lineage);
        RollBeginArea(lineage);
        LimitLooksToGender();
        RecountTraitCredits();
        if (AptitudeSpend(lineage) < 0)
            RestartAptitudes(lineage);
    }

    private void ChooseGender(uint genderTag)
    {
        _gender = genderTag;
        LimitLooksToGender();
    }

    private void ImposeBlueprint(GenesisHeritageOptions lineage)
    {
        _boltedTraits = 0u;
        if (_lineage == (uint)GenesisHeritage.Olthoi
            || _lineage == (uint)GenesisHeritage.OlthoiAcid)

            _blueprint = 0u;

        if (_lineage is 0 || _gender is 0 || _blueprint == SimToonGenesisCapture.BlueprintUnset)
            return;
        if (_blueprint >= (uint)lineage.Templates.Count)
        {
            _blueprint = SimToonGenesisCapture.BlueprintUnset;
            return;
        }

        var rank = lineage.Templates[(int)_blueprint];
        _attrs = rank.Attributes;

        RestartAptitudes(lineage);
        foreach (uint aptitudeIdent in rank.NormalSkills)
            ImposeBlueprintAptitude(lineage, aptitudeIdent, GenesisSkillTrack.Trained);
        foreach (uint aptitudeIdent in rank.PrimarySkills)
            ImposeBlueprintAptitude(lineage, aptitudeIdent, GenesisSkillTrack.Specialized);
    }

    private void ImposeBlueprintAptitude(
        GenesisHeritageOptions lineage,
        uint aptitudeIdent,
        GenesisSkillTrack markClass)
    {
        if (aptitudeIdent is 0 || aptitudeIdent >= GenesisSkillTrackSet.SlotCount)
            return;

        var earlier = _aptitudes[aptitudeIdent];
        int leftover = _aptitudeCreditsLeft;
        if (earlier == GenesisSkillTrack.Trained
            && AptitudePriceOf(lineage, aptitudeIdent, out int earlierTrained, out _))
        {
            leftover += earlierTrained;
        }
        else if (earlier == GenesisSkillTrack.Specialized
            && AptitudePriceOf(lineage, aptitudeIdent, out _, out int earlierSpecialized))
        {
            leftover += earlierSpecialized;
        }

        if (!AptitudePriceOf(lineage, aptitudeIdent, out int trainedPrice, out int specializedPrice))
            return;

        int charge = markClass switch
        {
            GenesisSkillTrack.Specialized => specializedPrice,
            GenesisSkillTrack.Trained => trainedPrice,
            _ => 0,
        };
        leftover -= charge;
        if (leftover < 0)
            return;

        _aptitudes[aptitudeIdent] = markClass;
        _aptitudeCreditsLeft = leftover;
    }

    private void RollBeginArea(GenesisHeritageOptions lineage)
    {
        if (lineage.PrimaryStartAreaIndices.Count is 0)
            return;
        int contender = lineage.PrimaryStartAreaIndices[
            _random.Next(lineage.PrimaryStartAreaIndices.Count)];
        _beginArea = contender >= 0 && contender < _knobs.StarterAreas.Count
            ? contender
            : -1;
    }

    private CharacterForge.WireRequest ConstructReq()
    {
        return new(
        _lineage,
        _gender,
        new CharacterForge.Looks(
            _looks.EyesStrip,
            _looks.NoseStrip,
            _looks.MouthStrip,
            _looks.HairColor,
            _looks.EyeColor,
            _looks.HairStyle,
            _looks.HeadgearStyle,
            _looks.HeadgearColor,
            _looks.ShirtStyle,
            _looks.ShirtColor,
            _looks.TrousersStyle,
            _looks.TrousersColor,
            _looks.FootwearStyle,
            _looks.FootwearColor,
            _looks.SkinShade,
            _looks.HairShade,
            _looks.HeadgearShade,
            _looks.ShirtShade,
            _looks.TrousersShade,
            _looks.FootwearShade),
        _blueprint,
        new CharacterForge.Attrs(
            checked((uint)_attrs.Strength),
            checked((uint)_attrs.Endurance),
            checked((uint)_attrs.Coordination),
            checked((uint)_attrs.Quickness),
            checked((uint)_attrs.Focus),
            checked((uint)_attrs.Self)),
        _socket,
        0u,
        _label,
        checked((uint)(_beginArea < 0 ? 0 : _beginArea)),
        IsAdmin: false,
        IsEnvoy: false);
    }

    private uint[] AptitudeArr()
    {
        var wire = _aptitudes.ToWireClasses();
        uint[] arr = new uint[wire.Count];
        for (int idx = 0; idx < arr.Length; ++idx)
            arr[idx] = wire[idx];
        return arr;
    }

    private void Wipe()
    {
        _lineage = 0;
        _gender = 0;
        _looks = SimToonGenesisAppearance.Default;
        _blueprint = SimToonGenesisCapture.BlueprintUnset;
        _attrs = default;
        _boltedTraits = 0u;
        _traitCredits = 0u;
        _traitCreditsLeft = 0;
        _aptitudeCredits = 0u;
        _aptitudeCreditsLeft = 0;
        for (uint idx = 1; idx < GenesisSkillTrackSet.SlotCount; ++idx)
            _aptitudes[idx] = GenesisSkillTrack.Inactive;
        _label = string.Empty;
        _beginArea = -1;
        _socket = 0u;
        _expectingVerdict = false;
        _refusal = SimToonGenesisLocalRefusal.None;
        _rejection = null;
        _built = null;
    }

    private void Publish(SimToonGenesisDiffKind sort)
    {
        SimEpochTicket gen;
        long rev;
        lock (_latch)
        {
            if (_destroyed)
                return;
            gen = _epoch;
            rev = _rev;
        }
        _signals.Publish(gen, rev, sort);
    }

    private void Live() =>
        ObjectDisposedException.ThrowIf(_destroyed, this);
}
