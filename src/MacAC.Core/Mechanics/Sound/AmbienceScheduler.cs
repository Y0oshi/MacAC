using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Sound;

public readonly record struct AmbienceTrigger(
    AmbienceVoice Instance,
    float Volume,
    Vector3? Position);

public sealed class AmbienceScheduler(IAudioDice? rng = null)
{
    private readonly PriorityQueue<AmbienceVoice, double> _deadlines = new();
    private readonly List<AmbienceVoice> _voices = [];
    private readonly IAudioDice _dice = rng ?? new AudioDice();

    /// <summary>Every live voice, in the order it was first seen.</summary>
    public IReadOnlyList<AmbienceVoice> Instances => _voices;

    public int QueuedCount => _deadlines.Count;

    public float SumSfxTally { get; private set; }

    public void CommenceReassemble()
    {
        foreach (AmbienceVoice voice in _voices)
            voice.RestartTally();
        SumSfxTally = 0f;
    }

    public AmbienceVoice Follow(AmbienceCue descriptor, uint sfxChartDid)
    {
        foreach (AmbienceVoice voice in _voices)
        {
            if (voice.Descriptor == descriptor && voice.SfxChartDid == sfxChartDid)
                return voice;
        }
        AmbienceVoice fresh = new AmbienceVoice(descriptor, sfxChartDid);
        _voices.Add(fresh);
        return fresh;
    }

    public void ContributeChamber<TTable>(Vector3 shift, TTable chart, Func<TTable, int, AmbienceCue> descriptorAt)
        where TTable : AmbientSoundSet
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(descriptorAt);

        float weight = AmbienceTuning.CalcWeight(shift);
        if (weight <= 0f)
            return;

        var bearing = AmbienceTuning.CalcDir(shift);
        SumSfxTally += weight;
        for (int idx = 0; idx < chart.Sounds.Count; ++idx)
            Follow(descriptorAt(chart, idx), chart.SoundBookId).AppendTo(weight, shift, bearing);
    }

    public void Contribute(AmbienceVoice inst, Vector3 shift)
    {
        ArgumentNullException.ThrowIfNull(inst);
        float weight = AmbienceTuning.CalcWeight(shift);
        if (weight <= 0f)
            return;
        inst.AppendTo(weight, shift, AmbienceTuning.CalcDir(shift));
        SumSfxTally += weight;
    }

    public void FinishReassemble(double instant, ICollection<AmbienceTrigger>? firings = null, Vector3 listenerLocus = default)
    {
        foreach (AmbienceVoice voice in _voices)
            voice.RefreshSfx(SumSfxTally);

        foreach (AmbienceVoice voice in _voices)
        {
            if (!voice.OnFifo && voice.CanHear())
                Fire(voice, instant, firings, listenerLocus);
        }
    }

    public void Tick(double instant, ICollection<AmbienceTrigger> firings, Vector3 listenerLocus)
    {
        ArgumentNullException.ThrowIfNull(firings);
        while (_deadlines.TryPeek(out _, out double due) && due < instant)
        {
            var voice = _deadlines.Dequeue();
            voice.OnFifo = false;
            if (voice.CanHear())
                Fire(voice, instant, firings, listenerLocus);
        }
    }

    public void Clear()
    {
        foreach (AmbienceVoice voice in _voices)
            voice.OnFifo = false;
        _voices.Clear();
        _deadlines.Clear();
        SumSfxTally = 0f;
    }

    private void Fire(AmbienceVoice voice, double instant, ICollection<AmbienceTrigger>? firings, Vector3 listenerLocus)
    {
        if (firings is not null && voice.PlayInstant(_dice))
        {
            Vector3? at = voice.TryFetchSfxLocus(listenerLocus, _dice, out Vector3 spot) ? spot : null;
            firings.Add(new AmbienceTrigger(voice, voice.FetchVolume(), at));
        }
        _deadlines.Enqueue(voice, instant + voice.FetchPlayInterval(_dice));
        voice.OnFifo = true;
    }
}
