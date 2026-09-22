using MacAC.Client.Graphics.Batching;

namespace MacAC.Client.Graphics;

// Durable, per-emitter teardown ledger
internal sealed class MoteEmitterRetirementLedger(
    Action<int> releaseMesh,
    Action<int> removeGfxInfo,
    Action<int> releaseTextureOwner,
    Action<Exception>? dossierMiss = null)
{
    private sealed class RetirementLedger
    {
        public bool TriMeshReleased;
        public bool GfxDetailsRemoved;
        public bool TextureHolderReleased;
        public bool MissReported;
    }

    private readonly Action<int> _freeTriMesh = releaseMesh ?? throw new ArgumentNullException(nameof(releaseMesh));
    private readonly Action<int> _dropGfxDetails = removeGfxInfo ?? throw new ArgumentNullException(nameof(removeGfxInfo));
    private readonly Action<int> _freeTextureHolder = releaseTextureOwner ?? throw new ArgumentNullException(nameof(releaseTextureOwner));
    private readonly Action<Exception>? _dossierMiss = dossierMiss;
    private readonly Dictionary<int, RetirementLedger> _queued = [];
    private readonly HashSet<int> _advancingHnds = [];

    internal int QueuedTally => _queued.Count;

    public void CommenceSunset(int spoutHnd)
    {
        if (_advancingHnds.Contains(spoutHnd))
            return;
        if (!_queued.TryGetValue(spoutHnd, out RetirementLedger? phase))
        {
            phase = new RetirementLedger();
            _queued.Add(spoutHnd, phase);
        }
        Advance(spoutHnd, phase);
    }

    public void ReattemptQueued()
    {
        if (_queued.Count is 0)
            return;

        int[] hnds = [.. _queued.Keys];
        for (int idx = 0; idx < hnds.Length; ++idx)
        {
            if (_queued.TryGetValue(hnds[idx], out RetirementLedger? phase))
                Advance(hnds[idx], phase);
        }
    }

    public void CompleteOrThrow()
    {
        ReattemptQueued();
        if (_queued.Count is not 0)
            throw new InvalidOperationException(
                $"{_queued.Count} particle-emitter teardown obligation(s) remain incomplete");
    }

    private void Advance(int spoutHnd, RetirementLedger phase)
    {
        if (!_advancingHnds.Add(spoutHnd))
            return;

        List<Exception>? misses = null;
        try
        {
            if (!phase.TriMeshReleased)
            {
                try
                {
                    _freeTriMesh(spoutHnd);
                    phase.TriMeshReleased = true;
                }
                catch (Exception problem)
                {
                    if (problem is TriMeshRefAlterationFault { AlterationSealed: true })
                        phase.TriMeshReleased = true;
                    (misses ??= []).Add(problem);
                }
            }

            if (!phase.GfxDetailsRemoved)
            {
                try
                {
                    _dropGfxDetails(spoutHnd);
                    phase.GfxDetailsRemoved = true;
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }

            if (!phase.TextureHolderReleased)
            {
                try
                {
                    _freeTextureHolder(spoutHnd);
                    phase.TextureHolderReleased = true;
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }

            if (phase.TriMeshReleased && phase.GfxDetailsRemoved && phase.TextureHolderReleased)
                _queued.Remove(spoutHnd);

            if (misses is not null && !phase.MissReported)
            {
                phase.MissReported = true;
                _dossierMiss?.Invoke(new AggregateException(
                    $"Particle emitter {spoutHnd} teardown will be retried",
                    misses));
            }
        }
        finally
        {
            _advancingHnds.Remove(spoutHnd);
        }
    }
}
