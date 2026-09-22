using MacAC.Client.Sound;
using MacAC.Mechanics.Targeting;
using MacAC.Sim.Actors;
using MacAC.Sim.Realm;

namespace MacAC.Client.Paging;

public interface IRealmEpochAvailability
{
    bool IsRealmOnHand { get; }
    long QuiescedGen { get; }
}

internal sealed class AlwaysAvailableRealmEpoch
    : IRealmEpochAvailability
{
    public static AlwaysAvailableRealmEpoch Instance { get; } = new();
    public bool IsRealmOnHand => true;
    public long QuiescedGen => 0;
}

internal sealed class RealmEpochAvailabilityLedger(
    SimRealmCrossingLedger transit)
        : IRealmEpochAvailability
{
    private readonly SimRealmCrossingLedger _passage = transit ?? throw new ArgumentNullException(nameof(transit));

    public bool IsRealmOnHand =>
        _passage.IsRealmSimulationOnHand;

    public long QuiescedGen =>
        IsRealmOnHand ? 0 : _passage.Snapshot.Generation;
}

internal readonly record struct RealmEpochQuiescenceEdge(
    bool ShouldApply,
    bool ClearWorldSelection);

internal sealed class RealmEpochQuiescence(
    PickPhase selection,
    GpuRealmPhase world,
    Func<uint, SimActorKey?> resolveProjectionKey,
    IRealmAudioQuiescence? sound)
{
    private readonly PickPhase _pick = selection ?? throw new ArgumentNullException(nameof(selection));
    private readonly GpuRealmPhase _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly Func<uint, SimActorKey?> _locateProjTag = resolveProjectionKey
            ?? throw new ArgumentNullException(nameof(resolveProjectionKey));
    private readonly IRealmAudioQuiescence? _sound = sound;
    private bool _fxListQuiesced;
    private bool _pickRimSealed;
    private bool _soundSuspended;
    private bool _soundChangeoverEngaged;

    public RealmEpochQuiescenceEdge GrabCommence()
    {
        if (_fxListQuiesced)
            return default;

        uint? chosen = _pick.ChosenObjectTag;
        bool wipeRealmPick =
            chosen is uint chosenOid
            && _locateProjTag(chosenOid) is SimActorKey tag
            && _world.IsOnlineActorShown(tag);
        return new RealmEpochQuiescenceEdge(
            ShouldApply: true,
            wipeRealmPick);
    }

    public void SealCommence(in RealmEpochQuiescenceEdge rim)
    {
        if (!rim.ShouldApply)
            return;

        _fxListQuiesced = true;
        if (!_pickRimSealed)
        {
            _pickRimSealed = true;
            if (rim.ClearWorldSelection)
            {
                _pick.Clear(
                    PickChangeSource.System,
                    PickChangeReason.Cleared);
            }
        }

        SettleSound();
    }

    public void WatchReleased()
    {
        if (!_fxListQuiesced
            && !_pickRimSealed
            && !_soundSuspended)

            return;

        _fxListQuiesced = false;
        SettleSound();
        if (!_fxListQuiesced && !_soundSuspended)
            _pickRimSealed = false;
    }

    private void SettleSound()
    {
        if (_sound is null || _soundChangeoverEngaged)
            return;

        _soundChangeoverEngaged = true;
        try
        {
            while (_soundSuspended != _fxListQuiesced)
            {
                bool suspend = _fxListQuiesced;
                if (suspend)
                    _sound.SuspendRealmSound();
                else
                    _sound.ReactivateRealmSound();
                _soundSuspended = suspend;
            }
        }
        finally
        {
            _soundChangeoverEngaged = false;
        }
    }
}
