using MacAC.Dat;
using MacAC.Client.Realm;
using MacAC.Mechanics.Effects;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics.Effects;

public sealed class ActorEffectProfile : IOnlineActorEffectProfile
{
    private ActorEffectProfile(RigSpec rig)
    {
        RigDefaultProgramDid = StandardizeKineticsProgramDid(rig.DefaultEffectId);
        LatestKineticsProgramChartDid = StandardizeChartDid((uint)rig.DefaultEffectBookId);
        LatestSfxChartDid = StandardizeSfxChartDid((uint)rig.DefaultSoundBookId);
    }

    public uint? RigDefaultProgramDid { get; }
    public uint? LatestKineticsProgramChartDid { get; private set; }
    public uint? LatestSfxChartDid { get; private set; }
    public uint RawDefaultProgramKind { get; private set; }
    public float DefaultProgramIntensity { get; private set; }
    public bool HasNetworkBlurb { get; private set; }

    public static ActorEffectProfile BuildDatStatic(RigSpec rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        return new ActorEffectProfile(rig);
    }

    public static ActorEffectProfile BuildOnline(
        RigSpec rig,
        KineticSpawnData kinetics)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ActorEffectProfile profile = new ActorEffectProfile(rig);
        profile.ImposeNetworkBlurb(kinetics);
        return profile;
    }

    public void ImposeNetworkBlurb(KineticSpawnData kinetics)
    {
        LatestKineticsProgramChartDid = StandardizeChartDid(
            kinetics.PhysicsScriptTableId.GetValueOrDefault());
        LatestSfxChartDid = StandardizeSfxChartDid(
            kinetics.SoundTableId.GetValueOrDefault());
        RawDefaultProgramKind = kinetics.DefaultScriptType.GetValueOrDefault();
        DefaultProgramIntensity = kinetics.DefaultScriptIntensity.GetValueOrDefault();
        HasNetworkBlurb = true;
    }

    private static uint? StandardizeKineticsProgramDid(uint did) =>
        KineticScriptTableLookup.IsKineticsProgramDid(did) ? did : null;

    private static uint? StandardizeChartDid(uint did) =>
        KineticScriptTableLookup.IsKineticsProgramChartDid(did) ? did : null;

    private static uint? StandardizeSfxChartDid(uint did) =>
        (did & 0xFF000000u) == 0x20000000u ? did : null;
}
