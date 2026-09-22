using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal sealed class StagedLandblockContactEpoch : IDisposable
{
    internal const int UpperConcurrentImpactPreparations = 256;
    private readonly SimKineticsLedger _holder;
    private readonly SimContactIntake _admission;
    private readonly List<uint> _keptHolderIdents = new();
    private readonly HashSet<uint> _keptHolderSet = new();
    private ProxyRegistry.RefloodOwnerScan? _keptHolderScan;
    private KineticEngine.LandblockSwapBuilder? _sealBuilder;
    private KineticEngine.BakedKineticEngineLandblock? _sealedSubstitute;
    private bool _destroyed;

    internal StagedLandblockContactEpoch(
        SimKineticsLedger holder,
        SimContactIntake admission,
        KineticEngine.ContactStagingBuilder loadingBuilder,
        long series)
    {
        _holder = holder;
        _admission = admission;
        ArgumentNullException.ThrowIfNull(loadingBuilder);
        DataCache = loadingBuilder.StagingCache;
        Engine = loadingBuilder.LoadingEngine;
        Series = series;
    }

    internal KineticAssetCache DataCache { get; }
    internal KineticEngine Engine { get; }
    internal long Series { get; }
    internal uint[] GfxObjectIdents { get; private set; } = Array.Empty<uint>();
    internal uint[] RigIdents { get; private set; } = Array.Empty<uint>();
    internal bool IsDestroyed => _destroyed;
    internal bool KeptHolderGrabDone { get; private set; }
    internal bool IsSealed => _sealedSubstitute is not null;
    internal bool IsPrimedForActivation => _sealedSubstitute is not null;

    public void Dispose()
    {
        if (_destroyed)
            return;
        Engine.Clear();
        _keptHolderScan?.Dispose();
        _keptHolderScan = null;
        _keptHolderIdents.Clear();
        _keptHolderSet.Clear();
        _sealBuilder?.Dispose();
        _sealBuilder = null;
        _sealedSubstitute = null;
        _destroyed = true;
    }

    internal SimContactStagingStep ProgressLoadingReplicate()
    {
        SecureUsable();
        return new SimContactStagingStep(
            Completed: true,
            WorkUnits: 0);
    }

    internal bool Fits(
        SimKineticsLedger holder,
        SimContactIntake admission)
    {
        return ReferenceEquals(_holder, holder)
        && ReferenceEquals(_admission, admission);
    }

    internal void AssignAssetClosure(uint[] gfxObjectIds, uint[] setupIds)
    {
        SecureUsable();
        GfxObjectIdents = gfxObjectIds
            ?? throw new ArgumentNullException(nameof(gfxObjectIds));
        RigIdents = setupIds ?? throw new ArgumentNullException(nameof(setupIds));
    }

    internal void RenewKeptHolder(uint holderIdent)
    {
        SecureUsable();
        SecureKeptHolder(holderIdent);
        _sealBuilder?.RenewKeptHolder(holderIdent);
    }

    internal IReadOnlyList<uint> KeptHolderIdents
    {
        get
        {
            SecureUsable();
            if (!KeptHolderGrabDone)
            {
                throw new InvalidOperationException(
                    "Retained collision-owner capture is incomplete");
            }
            return _keptHolderIdents;
        }
    }

    internal SimContactHolderCaptureStep ProgressKeptHolderGrab()
    {
        SecureUsable();
        if (KeptHolderGrabDone)
        {
            return new SimContactHolderCaptureStep(
                Completed: true,
                Restarted: false,
                HasOwner: false,
                OwnerId: 0u);
        }
        _keptHolderScan ??= _holder.Engine.ShadeObjects
            .BuildKeptRefloodHolderScan(_admission.LandblockId);
        var hop =
            _keptHolderScan.Advance();
        if (hop.HasOwner)
            SecureKeptHolder(hop.OwnerId);
        if (hop.Completed)
        {
            _keptHolderScan.Dispose();
            _keptHolderScan = null;
            KeptHolderGrabDone = true;
        }
        return new SimContactHolderCaptureStep(
            KeptHolderGrabDone,
            Restarted: false,
            hop.HasOwner,
            hop.OwnerId);
    }

    internal void RestartKeptHolderGrab()
    {
        SecureUsable();
        _keptHolderScan?.Dispose();
        _keptHolderScan = null;
        _keptHolderIdents.Clear();
        _keptHolderSet.Clear();
        KeptHolderGrabDone = false;
        _sealedSubstitute = null;
        _sealBuilder?.Dispose();
        _sealBuilder = null;
    }

    internal SimContactSealStep ProgressSeal()
    {
        SecureUsable();
        if (!KeptHolderGrabDone)
        {
            return new SimContactSealStep(
                Completed: false,
                Restarted: true,
                WorkUnits: 0);
        }

        _sealBuilder ??= _holder.Engine.BuildLbSubstituteBuilder(
            Engine,
            _admission.LandblockId,
            GfxObjectIdents,
            RigIdents,
            _keptHolderIdents);
        int prior = _sealBuilder.JobUnits;
        bool finished = _sealBuilder.Advance();
        int jobUnits = _sealBuilder.JobUnits - prior;
        if (!finished)
        {
            return new SimContactSealStep(
                Completed: false,
                Restarted: false,
                jobUnits);
        }
        if (_sealBuilder.Prepared is null)
        {
            RestartKeptHolderGrab();
            return new SimContactSealStep(
                Completed: false,
                Restarted: true,
                jobUnits);
        }
        _sealedSubstitute = _sealBuilder.Prepared;
        return new SimContactSealStep(
            Completed: true,
            Restarted: false,
            jobUnits);
    }

    internal KineticEngine.BakedKineticEngineLandblock GrabSealedSubstitute()
    {
        SecureUsable();
        return _sealedSubstitute
            ?? throw new InvalidOperationException(
                "Collision generation has to be sealed prior to activation");
    }

    internal void FlagSealed()
    {
        SecureUsable();
        _sealBuilder = null;
        _sealedSubstitute = null;
        _keptHolderIdents.Clear();
        _keptHolderSet.Clear();
        _destroyed = true;
    }

    private void SecureKeptHolder(uint holderIdent)
    {
        if (_keptHolderSet.Add(holderIdent))
            _keptHolderIdents.Add(holderIdent);
    }

    private void SecureUsable()
    {
        if (_destroyed)
            throw new ObjectDisposedException(nameof(StagedLandblockContactEpoch));
    }
}
