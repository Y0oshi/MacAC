using System.Numerics;
using MacAC.Client.Graphics.Batching;

namespace MacAC.Client.Graphics;

internal enum MoteSubmissionKind
{
    Billboard,
    Mesh,
}

internal readonly record struct MoteSubmission(
    MoteSubmissionKind Kind,
    int DrawIndex,
    float DistanceSq,
    int Sequence);

internal readonly record struct PreparedMoteAlphaSubmission(
    CanonAlphaFifo Queue,
    CanonAlphaList List,
    ICanonAlphaDrawSource Source,
    MotePainter Owner,
    MoteSubmissionKind Kind,
    int DrawIndex,
    Matrix4x4 LensMirror,
    bool OverrideClipmap,
    float DistanceSq,
    int Sequence)
{
    internal void Affix()
    {
        int ticket = Owner.EarmarkReadiedRelayPostponedMote(
            Kind,
            DrawIndex,
            LensMirror);
        bool approved;
        try
        {
            approved = Queue.TryAffix(List, Source, ticket, OverrideClipmap);
        }
        catch
        {
            Owner.RevertReadiedRelayPostponedMote(ticket);
            throw;
        }

        if (!approved)
            Owner.RevertReadiedRelayPostponedMote(ticket);
    }
}

internal static class MoteSubmissionOrdering
{
    public static void Sort(List<MoteSubmission> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        submissions.Sort(static (left, right) =>
        {
            int gap = right.DistanceSq.CompareTo(left.DistanceSq);
            return gap is not 0
                ? gap
                : left.Sequence.CompareTo(right.Sequence);
        });
    }
}

internal sealed class MoteMeshReferenceLedger(Action<uint> increment, Action<uint> decrement) : IDisposable
{
    private sealed class ReferenceLedger
    {
        public required uint GfxObjId { get; init; }
        public bool Desired { get; set; }
        public bool Held { get; set; }
        public bool Reconciling { get; set; }
    }

    private readonly Action<uint> _increment = increment ?? throw new ArgumentNullException(nameof(increment));
    private readonly Action<uint> _decrement = decrement ?? throw new ArgumentNullException(nameof(decrement));
    private readonly Dictionary<int, ReferenceLedger> _referencesBySpout = [];
    private bool _teardownAsked;
    private bool _destroyed;

    public void Register(int spoutHnd, uint gfxObjRefIdent)
    {
        ObjectDisposedException.ThrowIf(_teardownAsked, this);
        if (!_referencesBySpout.TryGetValue(spoutHnd, out ReferenceLedger? phase))
        {
            phase = new ReferenceLedger { GfxObjId = gfxObjRefIdent };
            _referencesBySpout.Add(spoutHnd, phase);
        }
        else if (phase.GfxObjId != gfxObjRefIdent)
        {
            throw new InvalidOperationException(
                $"Particle emitter {spoutHnd} is by now associated with " +
                $"GfxObj 0x{phase.GfxObjId:X8}, not 0x{gfxObjRefIdent:X8}.");
        }

        phase.Desired = true;
        Reconcile(spoutHnd, phase);
    }

    public void Release(int spoutHnd)
    {
        if (_destroyed || !_referencesBySpout.TryGetValue(spoutHnd, out ReferenceLedger? phase))
            return;

        phase.Desired = false;
        Reconcile(spoutHnd, phase);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        _teardownAsked = true;
        List<Exception>? misses = null;
        int[] spoutHnds = [.. _referencesBySpout.Keys];
        for (int idx = 0; idx < spoutHnds.Length; ++idx)
        {
            int spoutHnd = spoutHnds[idx];
            if (!_referencesBySpout.TryGetValue(spoutHnd, out ReferenceLedger? phase))
                continue;

            phase.Desired = false;
            try
            {
                Reconcile(spoutHnd, phase);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }

        if (_referencesBySpout.Count is 0)
            _destroyed = true;

        if (misses is not null)
            throw new AggregateException(
                "One or more particle mesh references could not release",
                misses);
    }

    private void Reconcile(int spoutHnd, ReferenceLedger phase)
    {
        if (phase.Reconciling)
            return;

        phase.Reconciling = true;
        Exception? miss = null;
        try
        {
            while (phase.Desired != phase.Held)
            {
                if (phase.Desired)
                {
                    try
                    {
                        _increment(phase.GfxObjId);
                        phase.Held = true;
                    }
                    catch (TriMeshRefAlterationFault problem)
                    {
                        if (problem.AlterationSealed)
                        {
                            phase.Held = true;
                            miss ??= problem;
                            continue;
                        }
                        miss = problem;
                        break;
                    }
                    catch (Exception problem)
                    {
                        miss = problem;
                        break;
                    }
                }
                else
                {
                    try
                    {
                        _decrement(phase.GfxObjId);
                        phase.Held = false;
                    }
                    catch (TriMeshRefAlterationFault problem)
                    {
                        if (problem.AlterationSealed)
                        {
                            phase.Held = false;
                            miss ??= problem;
                            continue;
                        }
                        miss = problem;
                        break;
                    }
                    catch (Exception problem)
                    {
                        miss = problem;
                        break;
                    }
                }
            }
        }
        finally
        {
            phase.Reconciling = false;
            if (!phase.Desired && !phase.Held)
                _referencesBySpout.Remove(spoutHnd);
            if (_teardownAsked && _referencesBySpout.Count is 0)
                _destroyed = true;
        }

        if (miss is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(miss).Throw();
    }
}
