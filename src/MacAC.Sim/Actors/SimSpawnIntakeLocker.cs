using System.Collections.Immutable;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Actors;

internal static class SimSpawnIntakeLocker
{
    internal static RealmSession.MoverSpawn Freeze(in RealmSession.MoverSpawn summon)
    {
        return summon with
        {
            AnimPartChanges = summon.AnimPartChanges.ToImmutableArray(),
            TextureChanges = summon.TextureChanges.ToImmutableArray(),
            SubPalettes = summon.SubPalettes.ToImmutableArray(),
            MotionState = Freeze(summon.MotionState),
            Physics = Freeze(summon.Physics),
        };
    }

    internal static ObjDescNotice.Parsed Freeze(in ObjDescNotice.Parsed refresh) => refresh with { ModelData = Freeze(refresh.ModelData) };

    internal static RealmSession.MoverMotionUpdate Freeze(in RealmSession.MoverMotionUpdate refresh) => refresh with { MotionState = Freeze(refresh.MotionState) };

    internal static SimSpawnTailAction Freeze(in SimSpawnTailAction act)
    {
        return act with
        {
            Description = Freeze(act.Description),
            ObjDesc = act.ObjDesc is { } objRefDsc ? Freeze(objRefDsc) : null,
            Movement = act.Movement is { } travel ? Freeze(travel) : null,
            WeenieDescription = act.WeenieDescription is { } summon ? Freeze(summon) : null,
        };
    }

    private static ObjectCreation.SchemeBlob Freeze(in ObjectCreation.SchemeBlob model)
    {
        return model with
        {
            SubPalettes = model.SubPalettes.ToImmutableArray(),
            TextureChanges = model.TextureChanges.ToImmutableArray(),
            AnimPartChanges = model.AnimPartChanges.ToImmutableArray(),
        };
    }

    private static ObjectCreation.RemoteMotionState Freeze(in ObjectCreation.RemoteMotionState locomotion) =>
        locomotion with { Commands = locomotion.Commands?.ToImmutableArray() };

    private static ObjectCreation.RemoteMotionState? Freeze(ObjectCreation.RemoteMotionState? locomotion) =>
        locomotion is { } val ? Freeze(val) : null;

    private static KineticSpawnData? Freeze(KineticSpawnData? kinetics)
    {
        if (kinetics is not { } val)
            return null;

        KineticMovementData? travel = val.Movement is { } src
            ? src with { RawData = src.RawData.ToArray(), MotionState = Freeze(src.MotionState) }
            : null;
        ReadOnlyMemory<KineticAttachment>? descendants = val.Children is { } roster ? roster.ToArray() : null;
        return val with { Movement = travel, Children = descendants };
    }
}
