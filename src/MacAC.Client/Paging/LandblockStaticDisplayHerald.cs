using MacAC.Dat;
using MacAC.Extensibility.World;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.PluginHosting;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed class LandblockStaticDisplayPublication
{
    private readonly uint[] _sequencedPreviouslyEngagedIdents;

    internal LandblockStaticDisplayPublication(
        object holder,
        LandblockKineticsPublication kineticsBulletin,
        Dictionary<uint, RealmActor> actors,
        Dictionary<uint, EntityFrame> captures,
        IReadOnlyDictionary<uint, EntityFrame> precedingEngaged,
        HashSet<uint> previouslyEngagedIdents,
        uint[] sequencedPreviouslyEngagedIdents)
    {
        Owner = holder;
        KineticsBulletin = kineticsBulletin;
        _actors = actors;
        _captures = captures;
        PrecedingEngaged = precedingEngaged;
        PreviouslyEngagedIdents = previouslyEngagedIdents;
        _sequencedPreviouslyEngagedIdents = sequencedPreviouslyEngagedIdents;
        _substituteEngaged = new Dictionary<uint, EntityFrame>(
            kineticsBulletin.Build.Landblock.Entities.Count);
    }

    internal object Owner { get; }
    internal LandblockKineticsPublication KineticsBulletin { get; }
    internal IReadOnlyDictionary<uint, EntityFrame> PrecedingEngaged { get; }
    private readonly Dictionary<uint, RealmActor> _actors;

    internal Dictionary<uint, RealmActor> MutableActors => _actors;
    private readonly Dictionary<uint, EntityFrame> _captures;

    internal Dictionary<uint, EntityFrame> MutableCaptures => _captures;
    internal HashSet<uint> PreviouslyEngagedIdents { get; }
    internal IReadOnlyList<uint> SequencedPreviouslyEngagedIdents =>
        _sequencedPreviouslyEngagedIdents;
    internal IReadOnlyList<KeyValuePair<uint, EntityFrame>>
        SequencedCaptures
    { get; set; } =
            Array.Empty<KeyValuePair<uint, EntityFrame>>();
    private readonly Dictionary<uint, EntityFrame> _substituteEngaged;

    internal Dictionary<uint, EntityFrame> SubstituteEngaged => _substituteEngaged;
    internal int PrepCur { get; set; }
    internal bool PrepSealed { get; set; }
    internal int PrecedingTidyCur { get; set; }
    internal int ExtensionCur { get; set; }
    internal bool CommenceSealed { get; set; }
    internal bool WrapUpSealed { get; set; }

    public uint LandblockId => KineticsBulletin.LandblockId;
    public IReadOnlyDictionary<uint, RealmActor> Entities => _actors;
    public IReadOnlyDictionary<uint, EntityFrame> Captures => _captures;
}

public readonly record struct LandblockStaticDisplayTelemetry(
    long BeginCount,
    long CompleteCount,
    long LightReplacementCount,
    long PluginSpawnCount,
    long PluginRefreshCount,
    long PluginRemovalCount,
    int ActiveLandblockCount,
    int ActiveEntityCount);

public sealed class LandblockStaticDisplayHerald(
    LightingHookTap lighting,
    SeeThroughFadeKeeper translucency,
    RealmPlayPhase worldState,
    RealmSignals worldEvents)
{
    private readonly object _receiptHolder = new();
    private readonly LightingHookTap _illumination = lighting ?? throw new ArgumentNullException(nameof(lighting));
    private readonly SeeThroughFadeKeeper _seeThrough = translucency
            ?? throw new ArgumentNullException(nameof(translucency));
    private readonly RealmPlayPhase _realmPhase = worldState ?? throw new ArgumentNullException(nameof(worldState));
    private readonly RealmSignals _realmSignals = worldEvents ?? throw new ArgumentNullException(nameof(worldEvents));
    private readonly Dictionary<uint, Dictionary<uint, EntityFrame>>
        _engagedByLb = [];
    private readonly Dictionary<uint, uint> _lbByActorIdent = [];

    private long _commenceTally;
    private long _doneTally;
    private long _lampSubstituteTally;
    private long _extensionSummonTally;
    private long _extensionRenewTally;
    private long _extensionDeletionTally;

    public LandblockStaticDisplayTelemetry Diagnostics
    {
        get
        {
            return new(
        _commenceTally,
        _doneTally,
        _lampSubstituteTally,
        _extensionSummonTally,
        _extensionRenewTally,
        _extensionDeletionTally,
        _engagedByLb.Count,
        _lbByActorIdent.Count);
        }
    }

    public LandblockStaticDisplayPublication PrepareBulletin(
        LandblockKineticsPublication kineticsBulletin)
    {
        var bulletin =
            BuildBulletin(kineticsBulletin);
        while (!ProgressPrepOne(bulletin))
        {
        }
        return bulletin;
    }

    public void CommenceBulletin(LandblockStaticDisplayPublication bulletin)
    {
        VetReceipt(bulletin);
        if (!bulletin.PrepSealed)
            throw new InvalidOperationException(
                "Static presentation can't begin prior to preparation commits");
        while (!AdvanceBeginOne(bulletin))
        {
        }
    }

    public void ReadyActorPriorImpact(
        LandblockStaticDisplayPublication bulletin,
        RealmActor actor)
    {
        VetReceipt(bulletin);
        ArgumentNullException.ThrowIfNull(actor);
        if (!bulletin.CommenceSealed)
            throw new InvalidOperationException(
                "Static presentation can't prepare an entity prior to begin commits");
        if (actor.ServerGuid is not 0)
            return;
        if (!bulletin.Entities.TryGetValue(actor.Id, out RealmActor? anticipated)
            || !ReferenceEquals(anticipated, actor))
        {
            throw new InvalidOperationException(
                $"DAT-static entity 0x{actor.Id:X8} doesn't belong to this publication");
        }

        bool kept = bulletin.PreviouslyEngagedIdents.Contains(actor.Id);
        _illumination.WithdrawHolder(actor.Id, forgetPhase: !kept);
        if (!kept)
            _seeThrough.WipeActor(actor.Id);

        KineticDatBundle datBundle = bulletin.KineticsBulletin.Build.Landblock.PhysicsDats
            ?? KineticDatBundle.Empty;
        uint srcIdent = actor.SrcGfxObjRefOrRigIdent;
        if ((srcIdent & 0xFF000000u) == 0x02000000u
            && datBundle.Setups.TryGetValue(srcIdent, out var rig))
        {
            var lamps = LightInfoReader.Load(
                rig,
                holderIdent: actor.Id,
                actorLocus: actor.Position,
                actorSpin: actor.Rotation,
                chamberIdent: actor.ParentCellId ?? 0u);
            for (int idx = 0; idx < lamps.Count; ++idx)
                _illumination.EnrollPossessedLamp(lamps[idx]);
        }
        ++_lampSubstituteTally;
    }

    public void ConcludePublication(
        LandblockStaticDisplayPublication bulletin)
    {
        VetReceipt(bulletin);
        if (!bulletin.CommenceSealed
            || !bulletin.KineticsBulletin.WrapUpSealed)
        {
            throw new InvalidOperationException(
                "Static presentation needs completed physics publication");
        }
        while (!ProgressDoneOne(bulletin))
        {
        }
    }

    public void DropIllumination(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ServerGuid is 0)
            _illumination.WithdrawHolder(actor.Id);
    }

    public void DropSeeThrough(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ServerGuid is 0)
            _seeThrough.WipeActor(actor.Id);
    }

    public void DropExtensionProj(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ServerGuid is not 0)
            return;

        _realmPhase.DropByIdent(actor.Id);
        _realmSignals.DropActor(actor.Id);
        if (_lbByActorIdent.Remove(actor.Id, out uint lbIdent)
            && _engagedByLb.TryGetValue(
                lbIdent,
                out Dictionary<uint, EntityFrame>? engaged))
        {
            engaged.Remove(actor.Id);
            if (engaged.Count is 0)
                _engagedByLb.Remove(lbIdent);
        }
        ++_extensionDeletionTally;
    }

    internal bool FitsAssetList(
        LightingHookTap illumination,
        SeeThroughFadeKeeper seeThrough)
    {
        return ReferenceEquals(_illumination, illumination)
        && ReferenceEquals(_seeThrough, seeThrough);
    }

    internal LandblockStaticDisplayPublication BuildBulletin(
        LandblockKineticsPublication kineticsBulletin)
    {
        ArgumentNullException.ThrowIfNull(kineticsBulletin);

        uint canon = Canonicalize(kineticsBulletin.LandblockId);
        _engagedByLb.TryGetValue(
            canon,
            out Dictionary<uint, EntityFrame>? engaged);
        var actors = new Dictionary<uint, RealmActor>();
        var captures = new Dictionary<uint, EntityFrame>();
        HashSet<uint> earlier = engaged is not null
            ? [.. engaged.Keys]
            : [];
        uint[] sequencedEarlier = [.. earlier.Order()];
        return new LandblockStaticDisplayPublication(
            _receiptHolder,
            kineticsBulletin,
            actors,
            captures,
            engaged ?? [],
            earlier,
            sequencedEarlier);
    }

    internal bool ProgressPrepOne(
        LandblockStaticDisplayPublication bulletin)
    {
        VetReceipt(bulletin);
        if (bulletin.PrepSealed)
            return true;

        var src =
            bulletin.KineticsBulletin.Build.Landblock.Entities;
        uint canon = Canonicalize(bulletin.LandblockId);
        if (bulletin.PrepCur < src.Count)
        {
            RealmActor actor = src[bulletin.PrepCur];
            if (actor.ServerGuid is not 0)
            {
                bulletin.PrepCur++;
                return false;
            }
            if (bulletin.Entities.ContainsKey(actor.Id))
            {
                throw new InvalidOperationException(
                    $"Landblock 0x{canon:X8} contains duplicate DAT-static ID " +
                    $"0x{actor.Id:X8}.");
            }
            if (_lbByActorIdent.TryGetValue(actor.Id, out uint holder)
                && holder != canon)
            {
                throw new InvalidOperationException(
                    $"DAT-static ID 0x{actor.Id:X8} is by now owned by " +
                    $"landblock 0x{holder:X8}.");
            }
            if (bulletin.PrecedingEngaged.TryGetValue(
                    actor.Id,
                    out EntityFrame kept)
                && kept.SourceId != actor.SrcGfxObjRefOrRigIdent)
            {
                throw new InvalidOperationException(
                    $"Retained DAT-static ID 0x{actor.Id:X8} changed source " +
                    $"from 0x{kept.SourceId:X8} to " +
                    $"0x{actor.SrcGfxObjRefOrRigIdent:X8}.");
            }

            bulletin.MutableActors.Add(actor.Id, actor);
            bulletin.MutableCaptures.Add(actor.Id, Freeze(actor));
            bulletin.PrepCur++;
            return false;
        }

        bulletin.SequencedCaptures = bulletin.Captures
            .OrderBy(static duo => duo.Key)
            .ToArray();
        bulletin.PrepSealed = true;
        return true;
    }

    internal bool AdvanceBeginOne(
        LandblockStaticDisplayPublication bulletin)
    {
        VetReceipt(bulletin);
        if (bulletin.CommenceSealed)
            return true;

        if (bulletin.PrecedingTidyCur
            < bulletin.SequencedPreviouslyEngagedIdents.Count)
        {
            uint ident = bulletin.SequencedPreviouslyEngagedIdents[
                bulletin.PrecedingTidyCur];
            bool kept = bulletin.Entities.ContainsKey(ident);
            _illumination.WithdrawHolder(ident, forgetPhase: !kept);
            if (!kept)
            {
                _seeThrough.WipeActor(ident);
                _realmPhase.DropByIdent(ident);
                _realmSignals.DropActor(ident);
                _lbByActorIdent.Remove(ident);
                ++_extensionDeletionTally;
            }
            bulletin.PrecedingTidyCur++;
            return false;
        }

        bulletin.CommenceSealed = true;
        ++_commenceTally;
        return true;
    }

    internal bool ProgressDoneOne(
        LandblockStaticDisplayPublication bulletin)
    {
        VetReceipt(bulletin);
        if (!bulletin.CommenceSealed
            || !bulletin.KineticsBulletin.WrapUpSealed)
        {
            throw new InvalidOperationException(
                "Static presentation needs completed physics publication");
        }
        if (bulletin.WrapUpSealed)
            return true;

        if (bulletin.ExtensionCur < bulletin.SequencedCaptures.Count)
        {
            (uint ident, EntityFrame capture) =
                bulletin.SequencedCaptures[bulletin.ExtensionCur];
            _realmPhase.Add(capture);
            if (bulletin.PreviouslyEngagedIdents.Contains(ident))
            {
                _realmSignals.UpsertLatest(capture);
                ++_extensionRenewTally;
            }
            else
            {
                _realmSignals.TriggerActorSpawned(capture);
                ++_extensionSummonTally;
            }
            _lbByActorIdent[ident] = Canonicalize(bulletin.LandblockId);
            bulletin.SubstituteEngaged[ident] = capture;
            bulletin.ExtensionCur++;
            return false;
        }

        uint canon = Canonicalize(bulletin.LandblockId);
        if (bulletin.SubstituteEngaged.Count is 0)
            _engagedByLb.Remove(canon);
        else
            _engagedByLb[canon] = bulletin.SubstituteEngaged;
        bulletin.WrapUpSealed = true;
        ++_doneTally;
        return true;
    }

    private void VetReceipt(LandblockStaticDisplayPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!ReferenceEquals(publication.Owner, _receiptHolder))
        {
            throw new ArgumentException(
                "The static presentation receipt belongs to another publisher",
                nameof(publication));
        }
    }

    private static EntityFrame Freeze(RealmActor actor)
    {
        return new(
        Id: actor.Id,
        SourceId: actor.SrcGfxObjRefOrRigIdent,
        Position: actor.Position,
        Rotation: actor.Rotation);
    }

    private static uint Canonicalize(uint lbIdent) =>
        (lbIdent & 0xFFFF0000u) | 0xFFFFu;
}
