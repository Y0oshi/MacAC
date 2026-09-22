using System.Collections;

namespace MacAC.Mechanics.Effects;

public sealed partial class MoteSys
{
    /// <summary>Every emitter, in handle order. The struct enumerator allocates nothing.</summary>
    public readonly struct LiveEmitterWalk(MoteSys holder) : IEnumerable<MoteSpout>
    {
        public MechEnumerator GetEnumerator() => new(holder, holder._every);

        IEnumerator<MoteSpout> IEnumerable<MoteSpout>.GetEnumerator() => Boxed(holder, holder._every).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Boxed(holder, holder._every).GetEnumerator();

        public struct MechEnumerator(MoteSys holder, SortedSet<int> hnds)
        {
            private SortedSet<int>.Enumerator _cur = hnds.GetEnumerator();

            public MoteSpout Current { get; private set; } = null!;

            public bool MoveNext()
            {
                while (_cur.MoveNext())
                {
                    if (holder._spouts.TryGetValue(_cur.Current, out MoteSpout? emitter))
                    {
                        Current = emitter;
                        return true;
                    }
                }
                return false;
            }
        }
    }

    /// <summary>Drawable emitters of one pass, in handle order.</summary>
    public readonly struct DrawableEmitterWalk(MoteSys holder, ParticleDrawPass rasterizePass) : IEnumerable<MoteSpout>
    {
        private readonly SortedSet<int> _hnds = holder._passs[PassSocket(rasterizePass)].Drawable;

        public LiveEmitterWalk.MechEnumerator GetEnumerator() => new(holder, _hnds);

        IEnumerator<MoteSpout> IEnumerable<MoteSpout>.GetEnumerator() => Boxed(holder, _hnds).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Boxed(holder, _hnds).GetEnumerator();
    }

    /// <summary>Every live particle of every emitter, as (emitter, slot) pairs.</summary>
    public readonly struct LiveParticleWalk(MoteSys holder) : IEnumerable<(MoteSpout Emitter, int Index)>
    {
        public EnumeratorDef GetEnumerator() => new(holder);

        IEnumerator<(MoteSpout Emitter, int Index)> IEnumerable<(MoteSpout Emitter, int Index)>.GetEnumerator() =>
            BoxedMotes(holder).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => BoxedMotes(holder).GetEnumerator();

        private static IEnumerable<(MoteSpout Emitter, int Index)> BoxedMotes(MoteSys holder)
        {
            foreach (int hnd in holder._every)
            {
                if (!holder._spouts.TryGetValue(hnd, out MoteSpout? emitter))
                    continue;
                for (int idx = 0; idx < emitter.Particles.Length; ++idx)
                {
                    if (emitter.Particles[idx].Alive)
                        yield return (emitter, idx);
                }
            }
        }

        public struct EnumeratorDef(MoteSys holder)
        {
            private SortedSet<int>.Enumerator _cur = holder._every.GetEnumerator();
            private MoteSpout? _em;
            private int _socket = -1;

            public (MoteSpout Emitter, int Index) Current => (_em!, _socket);

            public bool MoveNext()
            {
                while (true)
                {
                    if (_em is not null)
                    {
                        for (_socket++; _socket < _em.Particles.Length; ++_socket)
                        {
                            if (_em.Particles[_socket].Alive)
                                return true;
                        }
                        _em = null;
                    }

                    if (!_cur.MoveNext())
                        return false;
                    if (!holder._spouts.TryGetValue(_cur.Current, out MoteSpout? emitter))
                        continue;
                    _em = emitter;
                    _socket = -1;
                }
            }
        }
    }

    private static IEnumerable<MoteSpout> Boxed(MoteSys holder, SortedSet<int> hnds)
    {
        foreach (int hnd in hnds)
        {
            if (holder._spouts.TryGetValue(hnd, out MoteSpout? emitter))
                yield return emitter;
        }
    }
}
