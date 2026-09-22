using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

// Completion receipts: the executed-action trace of a finished spawn, projected into the public
// placement-finish record
internal sealed partial class SimSpawnFollowupRunner
{
    private readonly Dictionary<SimActorKey, (ulong Sequence, SimSpawnExecutionStub Receipt, SimSpawnPlacementFinish Public)> _finishedStubs = [];

    internal bool TryFetchWrapUpReceipt(
        in SimPlacementMirrorTicket ticket,
        out SimSpawnExecutionStub receipt)
    {
        if (_finishedStubs.TryGetValue(
                ticket.Entity,
                out (ulong Sequence, SimSpawnExecutionStub Receipt,
                    SimSpawnPlacementFinish Public) listing)
            && listing.Sequence == ticket.Sequence)
        {
            receipt = listing.Receipt;
            return true;
        }
        receipt = default;
        return false;
    }

    internal bool TryFetchWrapUp(
        in SimPlacementMirrorTicket ticket,
        out SimSpawnPlacementFinish wrapUp)
    {
        if (_finishedStubs.TryGetValue(
                ticket.Entity,
                out (ulong Sequence, SimSpawnExecutionStub Receipt,
                    SimSpawnPlacementFinish Public) listing)
            && listing.Sequence == ticket.Sequence)
        {
            wrapUp = listing.Public;
            return true;
        }
        wrapUp = default;
        return false;
    }

    internal void DropWrapUpReceipt(SimActorKey tag, ulong series)
    {
        if (_finishedStubs.TryGetValue(
                tag,
                out (ulong Sequence, SimSpawnExecutionStub Receipt,
                    SimSpawnPlacementFinish Public) listing)
            && listing.Sequence == series)

            _finishedStubs.Remove(tag);
    }

    private static SimSpawnWarpHookPhase MapHookPhase(
        SimWarpHookPhase phase)
    {
        return phase switch
        {
            SimWarpHookPhase.None =>
                SimSpawnWarpHookPhase.None,
            SimWarpHookPhase.BeforePositionOperation =>
                SimSpawnWarpHookPhase.BeforePositionOperation,
            SimWarpHookPhase.AfterPositionOperation =>
                SimSpawnWarpHookPhase.AfterPositionOperation,
            SimWarpHookPhase.AfterEnterWorld =>
                SimSpawnWarpHookPhase.AfterEnterWorld,
            _ => throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                $"Unmapped {nameof(SimWarpHookPhase)} value - add an explicit arm to {nameof(MapHookPhase)} and to the public {nameof(SimSpawnWarpHookPhase)} projection"),
        };
    }

    private static SimSpawnPositionVerdict MapDisposition(
        SimSovereignPositionVerdict disposition)
    {
        return disposition switch
        {
            SimSovereignPositionVerdict.RejectedAuthority =>
                SimSpawnPositionVerdict.RejectedAuthority,
            SimSovereignPositionVerdict.RejectedData =>
                SimSpawnPositionVerdict.RejectedData,
            SimSovereignPositionVerdict.AwaitFreshPosition =>
                SimSpawnPositionVerdict.AwaitFreshPosition,
            SimSovereignPositionVerdict.NoPositionOperation =>
                SimSpawnPositionVerdict.NoPositionOperation,
            SimSovereignPositionVerdict.Interpolate =>
                SimSpawnPositionVerdict.Interpolate,
            SimSovereignPositionVerdict.SetPosition =>
                SimSpawnPositionVerdict.SetPosition,
            SimSovereignPositionVerdict.SetPositionSimple =>
                SimSpawnPositionVerdict.SetPositionSimple,
            _ => throw new ArgumentOutOfRangeException(
                nameof(disposition),
                disposition,
                $"Unmapped {nameof(SimSovereignPositionVerdict)} value - add an explicit arm to {nameof(MapDisposition)} and to the public {nameof(SimSpawnPositionVerdict)} projection"),
        };
    }

    private static SimSpawnPositionConstrainPhase MapConstrainPhase(
        SimPositionConstrainPhase phase)
    {
        return phase switch
        {
            SimPositionConstrainPhase.None =>
                SimSpawnPositionConstrainPhase.None,
            SimPositionConstrainPhase.BeforePositionOperation =>
                SimSpawnPositionConstrainPhase.BeforePositionOperation,
            SimPositionConstrainPhase.AfterPositionOperation =>
                SimSpawnPositionConstrainPhase.AfterPositionOperation,
            _ => throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                $"Unmapped {nameof(SimPositionConstrainPhase)} value - add an explicit arm to {nameof(MapConstrainPhase)} and to the public {nameof(SimSpawnPositionConstrainPhase)} projection"),
        };
    }

    private static SimSpawnPlacementFinish ProjectWrapUp(
        in SimSpawnExecutionStub receipt)
    {
        var trace = receipt.Trace;
        int locusTally = 0;
        for (int idx = 0; idx < trace.Length; ++idx)
        {
            if (trace[idx].Kind == SimSpawnExecutedActionKind.Position)
                ++locusTally;
        }

        ImmutableArray<SimSpawnPositionRouteFact> locusFacts;
        if (locusTally is 0)
        {
            locusFacts = ImmutableArray<SimSpawnPositionRouteFact>.Empty;
        }
        else
        {
            var builder = ImmutableArray.CreateBuilder<SimSpawnPositionRouteFact>(
                locusTally);
            for (int idx = 0; idx < trace.Length; ++idx)
            {
                var act = trace[idx];
                if (act.Kind != SimSpawnExecutedActionKind.Position)
                    continue;
                if (act.PositionDisposition is not { } disposition)
                {
                    throw new InvalidOperationException(
                        "A Position-kind executor trace entry must always " +
                        "carry a non-null PositionDisposition - " +
                        "PositionTrace (the sole producer of Kind.Position " +
                        "entries) always supplies route.Disposition");
                }
                builder.Add(new SimSpawnPositionRouteFact(
                    act.Sequence,
                    MapDisposition(disposition),
                    MapHookPhase(act.HookPhase),
                    MapConstrainPhase(act.ConstrainPhase),
                    act.StopInterpolating,
                    act.ZeroVelocity,
                    act.PreserveHeading,
                    act.SendPositionImmediately));
            }
            locusFacts = builder.MoveToImmutable();
        }

        return new SimSpawnPlacementFinish(
            receipt.Entity,
            receipt.FullCellId,
            MapHookPhase(receipt.TeleportHookPhase),
            locusFacts,
            receipt.ReplayedDeferredChildCount);
    }

    internal int QueuedWrapUpReceiptTally => _finishedStubs.Count;
}
