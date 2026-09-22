namespace MacAC.Mechanics.Effects;

public sealed partial class MoteSys
{
    private sealed class MechBucket
    {
        public int Count;
        public int LeadDrawable;
        public List<int>? MoreDrawable;

        public void AppendDrawable(int hnd)
        {
            if (LeadDrawable is 0)
            {
                LeadDrawable = hnd;
                return;
            }
            if (hnd < LeadDrawable)
                (LeadDrawable, hnd) = (hnd, LeadDrawable);
            List<int> more = MoreDrawable ??= [];
            int at = more.BinarySearch(hnd);
            if (at < 0)
                more.Insert(~at, hnd);
        }

        public void DropDrawable(int hnd)
        {
            if (LeadDrawable == hnd)
            {
                if (MoreDrawable is { Count: > 0 } more)
                {
                    LeadDrawable = more[0];
                    more.RemoveAt(0);
                }
                else
                {
                    LeadDrawable = 0;
                }
                return;
            }
            if (MoreDrawable is { } others)
            {
                int at = others.BinarySearch(hnd);
                if (at >= 0)
                    others.RemoveAt(at);
            }
        }

        public void DuplicateDrawableTo(List<int> into)
        {
            if (LeadDrawable is 0)
                return;
            into.Add(LeadDrawable);
            if (MoreDrawable is { Count: > 0 } more)
                into.AddRange(more);
        }
    }

    // The render indexes for one draw pass
    private sealed class PassIndex
    {
        public readonly SortedSet<int> Drawable = [];
        public readonly SortedSet<int> DrawableLoose = [];
        public readonly Dictionary<uint, MechBucket> ByHolder = [];
        public readonly Dictionary<uint, MechBucket> ByCell = [];

        public void Enrol(MoteSpout emitter)
        {
            MechBucket? holder = null;
            if (emitter.AffixedObjectIdent is not 0)
            {
                holder = Expand(ByHolder, emitter.AffixedObjectIdent);
                holder.Count++;
            }
            MechBucket chamber = Expand(ByCell, emitter.HolderChamberIdent);
            chamber.Count++;

            if (!IsDrawable(emitter))
                return;
            Drawable.Add(emitter.Handle);
            if (holder is null)
                DrawableLoose.Add(emitter.Handle);
            else
                holder.AppendDrawable(emitter.Handle);
            chamber.AppendDrawable(emitter.Handle);
        }

        public void Withdraw(MoteSpout emitter)
        {
            Drawable.Remove(emitter.Handle);
            if (emitter.AffixedObjectIdent is 0)
                DrawableLoose.Remove(emitter.Handle);
            else
                Contract(ByHolder, emitter.AffixedObjectIdent, emitter.Handle);
            Contract(ByCell, emitter.HolderChamberIdent, emitter.Handle);
        }

        public void ShiftChamber(MoteSpout emitter, uint chamberIdent)
        {
            bool drawable = IsDrawable(emitter);
            if (ByCell.TryGetValue(emitter.HolderChamberIdent, out MechBucket? was))
            {
                if (drawable)
                    was.DropDrawable(emitter.Handle);
                if (--was.Count is 0)
                    ByCell.Remove(emitter.HolderChamberIdent);
            }

            emitter.HolderChamberIdent = chamberIdent;
            MechBucket instant = Expand(ByCell, chamberIdent);
            instant.Count++;
            if (drawable)
                instant.AppendDrawable(emitter.Handle);
        }

        // Adds or removes the emitter from every drawable index when its drawability flipped
        public void Reindex(MoteSpout emitter, bool wasDrawable)
        {
            bool drawable = IsDrawable(emitter);
            if (wasDrawable == drawable)
                return;

            Toggle(Drawable, emitter.Handle, drawable);
            if (emitter.AffixedObjectIdent is 0)
                Toggle(DrawableLoose, emitter.Handle, drawable);
            else if (ByHolder.TryGetValue(emitter.AffixedObjectIdent, out MechBucket? holder))
                Toggle(holder, emitter.Handle, drawable);
            if (ByCell.TryGetValue(emitter.HolderChamberIdent, out MechBucket? chamber))
                Toggle(chamber, emitter.Handle, drawable);
        }

        private static MechBucket Expand(Dictionary<uint, MechBucket> bins, uint tag)
        {
            if (!bins.TryGetValue(tag, out MechBucket? bin))
                bins.Add(tag, bin = new MechBucket());
            return bin;
        }

        private static void Contract(Dictionary<uint, MechBucket> bins, uint tag, int hnd)
        {
            if (!bins.TryGetValue(tag, out MechBucket? bin))
                return;
            bin.DropDrawable(hnd);
            if (--bin.Count is 0)
                bins.Remove(tag);
        }

        private static void Toggle(SortedSet<int> set, int hnd, bool present)
        {
            if (present)
                set.Add(hnd);
            else
                set.Remove(hnd);
        }

        private static void Toggle(MechBucket bin, int hnd, bool present)
        {
            if (present)
                bin.AppendDrawable(hnd);
            else
                bin.DropDrawable(hnd);
        }
    }
}
