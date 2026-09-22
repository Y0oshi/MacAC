using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Actors;

// Read-only views over the entity index and the object table, plus the capture conversions they
// share
internal sealed class SimActorObjectLenses
{
    public SimActorObjectLenses(SimActorIndex actors, ClientThingChart objects)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(objects);
        Entities = new ActorLens(actors);
        Inventory = new StashLens(actors, objects);
    }

    public ISimActorLens Entities { get; }
    public ISimStashLens Inventory { get; }

    internal static SimActorCapture Freeze(SimActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        uint ownIdent = capture.OwnActorTag
            ?? throw new InvalidOperationException($"Canonical entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} has no Runtime identity");
        return new SimActorCapture(
            new SimActorIdentity(capture.ServerGuid, ownIdent, capture.Incarnation),
            capture.WholeChamberTag,
            (uint)capture.FinalKineticsCondition,
            AsLocus(capture.Snapshot.Position));
    }

    // An object as inventory sees it; the incarnation comes from the live entity unless the caller
    // pins one
    internal static SimStashItemCapture Freeze(ClientThing gear, SimActorIndex actors, ushort? preciseGen = null)
    {
        ArgumentNullException.ThrowIfNull(gear);
        ushort incarnation = preciseGen
            ?? (actors.TryFetchEngaged(gear.ObjectId, out SimActorRecord online) ? online.Incarnation : (ushort)0);
        return new SimStashItemCapture(
            gear.ObjectId,
            incarnation,
            gear.Name,
            gear.VesselTag,
            gear.VesselSlot,
            gear.WielderIdent,
            (uint)gear.CurrentlyEquippedLocale,
            gear.StackSize,
            gear.Value);
    }

    private static Locus? AsLocus(ObjectCreation.RemotePosition? p)
    {
        return p is { } position
            ? new Locus(position.LandblockId, new Vector3(position.PositionX, position.PositionY, position.PositionZ), new Quaternion(position.RotationX, position.RotationY, position.RotationZ, position.RotationW))
            : null;
    }

    private sealed class ActorLens(SimActorIndex ordinal) : ISimActorLens
    {
        public int Count => ordinal.Count;
        public int MaterializedCount => ordinal.ClaimedOwnIdentTally;

        public bool TryGet(uint srvOid, out SimActorCapture actor)
        {
            using var tenancy = ordinal.ObtainEngagedScan();
            bool located = ordinal.TryFetchEngaged(srvOid, out SimActorRecord capture);
            actor = located ? Freeze(capture) : default;
            return located;
        }

        public void Visit(ISimActorVisitor visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            using var tenancy = ordinal.ObtainEngagedScan();
            foreach (SimActorRecord capture in ordinal.ActiveRecords)
            {
                var actor = Freeze(capture);
                visitor.Tour(in actor);
            }
        }
    }

    private sealed class StashLens(SimActorIndex actors, ClientThingChart chart) : ISimStashLens
    {
        public int ObjectTally => chart.ObjectCount;
        public int VesselCount => chart.VesselTally;

        public bool TryGet(uint objectIdent, out SimStashItemCapture gear)
        {
            var located = chart.Get(objectIdent);
            gear = located is null ? default : Freeze(located, actors);
            return located is not null;
        }

        public void Visit(ISimStashVisitor visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            foreach (ClientThing gear in chart.Objects)
            {
                var grab = Freeze(gear, actors);
                visitor.Tour(in grab);
            }
        }
    }
}
