using MacAC.Dat;
using System.Numerics;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Effects;

public sealed record ProgramActivationDetails(
    uint ScriptId,
    IReadOnlyList<Matrix4x4> PartTransforms,
    ActorEffectProfile? EffectProfile = null,
    IReadOnlyList<bool>? PartAvailability = null,
    RigSpec? Setup = null,
    uint DefaultAnimationId = 0,
    bool UsesStaticAnimationWorkset = false);

public sealed class ActorProgramActivator(
    KineticScriptRunner scriptRunner,
    ParticleHookTap particleSink,
    ActorEffectPoseRegistry poses,
    Func<RealmActor, ProgramActivationDetails?> resolver,
    Action<uint, RealmActor, ActorEffectProfile>? enrollDatStaticFxHolder = null,
    Action<uint>? withdrawDatStaticFxHolder = null,
    Func<RealmActor, ProgramActivationDetails, bool>? enrollDatStaticAnimHolder = null,
    Action<uint>? withdrawDatStaticAnimHolder = null,
    Action<RealmActor, ProgramActivationDetails>? rebindDatStaticAnimHolder = null)
{
    private sealed class StaticOwnerLedger
    {
        public required RealmActor Entity;
        public required ProgramActivationDetails Info;
        public bool PosturePublished;
        public bool FxHolderRegistered;
        public bool AnimHolderRegistered;
        public bool ProgramBegun;
        public bool FreeBegun;
        public bool ProgramsStopped;
        public bool SpoutsStopped;
        public bool PostureRemoved;
        public RealmActor? QueuedSubstitute;
        public ProgramActivationDetails? QueuedSubstituteDetails;
        public bool SubstitutePosturePublished;
        public bool SubstituteFxHolderUpdated;
        public bool SubstituteAnimHolderUpdated;
        public bool SubstituteMooringUpdated;
    }

    private readonly KineticScriptRunner _programRunner = scriptRunner ?? throw new ArgumentNullException(nameof(scriptRunner));
    private readonly ParticleHookTap _moteDrain = particleSink ?? throw new ArgumentNullException(nameof(particleSink));
    private readonly ActorEffectPoseRegistry _postures = poses ?? throw new ArgumentNullException(nameof(poses));
    private readonly Func<RealmActor, ProgramActivationDetails?> _locator = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly Action<uint, RealmActor, ActorEffectProfile>? _enrollDatStaticFxHolder = enrollDatStaticFxHolder;
    private readonly Action<uint>? _withdrawDatStaticFxHolder = withdrawDatStaticFxHolder;
    private readonly Func<RealmActor, ProgramActivationDetails, bool>? _enrollDatStaticAnimHolder = enrollDatStaticAnimHolder;
    private readonly Action<uint>? _withdrawDatStaticAnimHolder = withdrawDatStaticAnimHolder;
    private readonly Action<RealmActor, ProgramActivationDetails>? _rebindDatStaticAnimHolder = rebindDatStaticAnimHolder;
    private readonly Dictionary<uint, StaticOwnerLedger> _staticHolders = [];

    public void OnRebindStatic(RealmActor earlier, RealmActor replacement)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(replacement);
        if (earlier.ServerGuid is not 0
            || replacement.ServerGuid is not 0
            || earlier.Id is 0
            || earlier.Id != replacement.Id)
        {
            throw new ArgumentException(
                "DAT-static rebind needs the same nonzero local ID",
                nameof(replacement));
        }

        uint tag = earlier.Id;
        if (!_staticHolders.TryGetValue(tag, out StaticOwnerLedger? phase))
            return;
        if (ReferenceEquals(phase.Entity, replacement))
            return;
        if (!ReferenceEquals(phase.Entity, earlier))
        {
            throw new InvalidOperationException(
                $"DAT-static owner 0x{tag:X8} doesn't match the retained incarnation");
        }
        if (phase.FreeBegun)
        {
            throw new InvalidOperationException(
                $"DAT-static owner 0x{tag:X8} can't rebind while teardown is pending");
        }

        if (phase.QueuedSubstitute is null)
        {
            ProgramActivationDetails substituteDetails = _locator(replacement)
                ?? throw new InvalidOperationException(
                    $"DAT-static owner 0x{tag:X8} lost its Setup activation data during rebind");
            VetLogicalRebind(phase, replacement, substituteDetails);
            phase.QueuedSubstitute = replacement;
            phase.QueuedSubstituteDetails = substituteDetails;
        }
        else if (!ReferenceEquals(phase.QueuedSubstitute, replacement))
        {
            throw new InvalidOperationException(
                $"DAT-static owner 0x{tag:X8} by now has another rebind pending");
        }

        var details = phase.QueuedSubstituteDetails!;
        if (phase.PosturePublished && !phase.SubstitutePosturePublished)
        {
            _postures.Publish(replacement, details.PartTransforms, details.PartAvailability);
            phase.SubstitutePosturePublished = true;
        }
        if (phase.FxHolderRegistered && !phase.SubstituteFxHolderUpdated)
        {
            _enrollDatStaticFxHolder!(tag, replacement, details.EffectProfile!);
            phase.SubstituteFxHolderUpdated = true;
        }
        if (phase.AnimHolderRegistered && !phase.SubstituteAnimHolderUpdated)
        {
            (_rebindDatStaticAnimHolder
                ?? throw new InvalidOperationException(
                    "DAT-static animation ownership has no retained-owner rebind callback"))(
                replacement,
                details);
            phase.SubstituteAnimHolderUpdated = true;
        }
        if (!phase.SubstituteMooringUpdated)
        {
            _programRunner.AssignHolderMooring(tag, replacement.Position);
            phase.SubstituteMooringUpdated = true;
        }

        OnRebindStaticRest(replacement, phase, details);
    }

    private void OnRebindStaticRest(RealmActor replacement, StaticOwnerLedger phase, ProgramActivationDetails details)
    {
        phase.Entity = replacement;
        phase.Info = details;
        phase.QueuedSubstitute = null;
        phase.QueuedSubstituteDetails = null;
        OnRebindStaticTail(phase);
    }

    private void OnRebindStaticTail(StaticOwnerLedger phase)
    {
        phase.SubstitutePosturePublished = false;
        phase.SubstituteFxHolderUpdated = false;
        phase.SubstituteAnimHolderUpdated = false;
        phase.SubstituteMooringUpdated = false;
    }

    public void OnBuild(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        uint tag = actor.Id;
        if (tag is 0)
            return;

        if (actor.ServerGuid is 0
            && _staticHolders.TryGetValue(tag, out StaticOwnerLedger? kept))
        {
            if (!ReferenceEquals(kept.Entity, actor))
            {
                throw new InvalidOperationException(
                    $"Dat-static RealmActor id 0x{tag:X8} is by now active; static allocators has to be globally unique");
            }
            if (kept.FreeBegun)
            {
                throw new InvalidOperationException(
                    $"Dat-static RealmActor id 0x{tag:X8} can't reactivate while teardown is pending");
            }

            EngageStaticHolder(kept);
            return;
        }

        var details = _locator(actor);
        if (details is null)
            return;

        if (actor.ServerGuid is 0)
        {
            StaticOwnerLedger phase = new StaticOwnerLedger
            {
                Entity = actor,
                Info = details,
            };
            _staticHolders.Add(tag, phase);
            EngageStaticHolder(phase);
            return;
        }

        _postures.Publish(actor, details.PartTransforms, details.PartAvailability);
        if (details.UsesStaticAnimationWorkset
            && details.DefaultAnimationId is not 0)

            _enrollDatStaticAnimHolder?.Invoke(actor, details);
        if (details.ScriptId is not 0)
            _programRunner.Play(details.ScriptId, tag, actor.Position);
    }

    public void OnDrop(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        uint tag = actor.Id;
        if (tag is 0)
            return;

        if (actor.ServerGuid is 0)
        {
            if (!_staticHolders.TryGetValue(tag, out StaticOwnerLedger? phase)
                || !ReferenceEquals(phase.Entity, actor))

                return;

            phase.FreeBegun = true;
            if (!phase.ProgramsStopped)
            {
                _programRunner.HaltAllForActor(tag);
                phase.ProgramsStopped = true;
            }
            if (!phase.SpoutsStopped)
            {
                _moteDrain.CeaseAllForActor(tag, fadeOut: false);
                phase.SpoutsStopped = true;
            }
            if (!phase.PostureRemoved)
            {
                _postures.Delete(tag);
                phase.PostureRemoved = true;
            }
            if (phase.AnimHolderRegistered)
            {
                _withdrawDatStaticAnimHolder?.Invoke(tag);
                phase.AnimHolderRegistered = false;
            }
            if (phase.FxHolderRegistered)
            {
                _withdrawDatStaticFxHolder?.Invoke(tag);
                phase.FxHolderRegistered = false;
            }

            _staticHolders.Remove(tag);
            return;
        }

        _withdrawDatStaticAnimHolder?.Invoke(tag);
        _programRunner.HaltAllForActor(tag);
        _moteDrain.CeaseAllForActor(tag, fadeOut: false);
        _postures.Delete(tag);
    }

    private static void VetLogicalRebind(
        StaticOwnerLedger phase,
        RealmActor substitute,
        ProgramActivationDetails substituteDetails)
    {
        var kept = phase.Info;
        bool keptAnim = kept.UsesStaticAnimationWorkset
            && kept.DefaultAnimationId is not 0;
        bool substituteAnim = substituteDetails.UsesStaticAnimationWorkset
            && substituteDetails.DefaultAnimationId is not 0;
        if (phase.Entity.SrcGfxObjRefOrRigIdent != substitute.SrcGfxObjRefOrRigIdent
            || kept.ScriptId != substituteDetails.ScriptId
            || keptAnim != substituteAnim
            || (keptAnim
                && kept.DefaultAnimationId != substituteDetails.DefaultAnimationId)
            || (kept.EffectProfile is null) != (substituteDetails.EffectProfile is null))
        {
            throw new InvalidOperationException(
                $"DAT-static owner 0x{phase.Entity.Id:X8} changed Setup lifetime during a retained rebind");
        }
    }

    private void EngageStaticHolder(StaticOwnerLedger phase)
    {
        RealmActor actor = phase.Entity;
        var details = phase.Info;
        uint tag = actor.Id;

        if (!phase.PosturePublished)
        {
            _postures.Publish(actor, details.PartTransforms, details.PartAvailability);
            phase.PosturePublished = true;
        }

        if (!phase.FxHolderRegistered
            && details.EffectProfile is { } profile
            && _enrollDatStaticFxHolder is not null)
        {
            _enrollDatStaticFxHolder(tag, actor, profile);
            phase.FxHolderRegistered = true;
        }

        if (!phase.AnimHolderRegistered
            && details.UsesStaticAnimationWorkset
            && details.DefaultAnimationId is not 0
            && _enrollDatStaticAnimHolder is not null)
        {
            phase.AnimHolderRegistered =
                _enrollDatStaticAnimHolder(actor, details);
        }

        if (!phase.ProgramBegun && details.ScriptId is not 0)
        {
            _programRunner.Play(details.ScriptId, tag, actor.Position);
            phase.ProgramBegun = true;
        }
    }
}
