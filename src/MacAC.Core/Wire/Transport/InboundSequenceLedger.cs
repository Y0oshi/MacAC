using System.Buffers.Binary;
using MacAC.Wire.Cryptography;

namespace MacAC.Wire.Transport;

internal sealed class InboundSequenceLedger
{
    internal const uint AceStartingWatermark = 1;
    internal const uint SanityPane = 0x7FFF;

    // An ISAAC word and the ordinal of the draw that produced it
    internal readonly record struct HeldWord(uint Word, ulong DrawOrder);

    public readonly record struct WireAdmission(bool Drop, uint? VerifyKey, ulong VerifyKeyDrawOrder)
    {
        public static WireAdmission Dropped => new(true, null, 0);

        public static WireAdmission Process(uint? verifyTag, ulong verifyTagPaintOrdering = ulong.MaxValue) => new(false, verifyTag, verifyTagPaintOrdering);
    }

    private readonly IsaacStream _isaac;
    private readonly LinkStats _stats;
    private readonly SortedDictionary<uint, HeldWord> _absent = new();
    private readonly PriorityQueue<uint, ulong> _spare = new();
    private readonly List<uint> _shiftTemp = [];
    private ulong _draws;

    public InboundSequenceLedger(IsaacStream incomingIsaac, LinkStats stats, uint startingWatermark = AceStartingWatermark)
    {
        ArgumentNullException.ThrowIfNull(incomingIsaac);
        ArgumentNullException.ThrowIfNull(stats);
        _isaac = incomingIsaac;
        _stats = stats;
        HighestIdentReceived = startingWatermark;
    }

    public uint HighestIdentReceived { get; private set; }

    public int NakTally => _absent.Count;

    public int ReclaimedWordTally => _spare.Count;

    public WireAdmission Admit(uint series, bool encrypted)
    {
        if (SequenceArith.IsNewer(series, unchecked(HighestIdentReceived + SanityPane)))
        {
            _stats.IncomingSanityDrops++;
            return WireAdmission.Dropped;
        }

        bool advances = SequenceArith.IsNewer(series, HighestIdentReceived);

        HeldWord? shelved = null;
        if (encrypted && !advances)
        {
            if (!_absent.Remove(series, out HeldWord pinned))
            {
                _stats.IncomingDupsDropped++;
                return WireAdmission.Dropped;
            }
            shelved = pinned;
        }

        if (advances)
        {
            // Encrypted ids draw their own word below; cleartext ones never draw, so the gap closes one later.
            uint halt = encrypted ? series : unchecked(series + 1u);
            for (uint ident = unchecked(HighestIdentReceived + 1u); ident != halt; ident = unchecked(ident + 1u))
            {
                if (ident is not 0)
                    Park(ident);
            }
            HighestIdentReceived = series;
        }

        if (!encrypted)
            return WireAdmission.Process(null);

        HeldWord mine = shelved ?? Draw();
        return WireAdmission.Process(mine.Word, mine.DrawOrder);
    }

    // Puts a word back on a missing id (after a failed verify) unless one is already there
    public void ReparkTag(uint series, uint tag, ulong paintOrdering)
    {
        if (_absent.TryAdd(series, new HeldWord(tag, paintOrdering)))
            _stats.TagsShelved++;
    }

    public void OnRejectRetransmit(ReadOnlySpan<byte> identOctets, int tally)
    {
        if (tally <= 0 || identOctets.Length < tally * 4)
            return;
        for (int idx = 0; idx < tally; ++idx)
            _absent.Remove(BinaryPrimitives.ReadUInt32LittleEndian(identOctets.Slice(idx * 4)));
    }

    public void DuplicateNakkedSequencesAscending(List<uint> dest, int upperTally = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach (uint ident in _absent.Keys)
        {
            if (dest.Count >= upperTally)
                break;
            dest.Add(ident);
        }
    }

    public void OnCleartextRejectSeries(uint series)
    {
        if (!_absent.Remove(series, out HeldWord freed))
            return;

        _shiftTemp.Clear();
        foreach ((uint ident, HeldWord pinned) in _absent)
        {
            if (pinned.DrawOrder > freed.DrawOrder)
                _shiftTemp.Add(ident);
        }
        _shiftTemp.Sort((a, b) => _absent[a].DrawOrder.CompareTo(_absent[b].DrawOrder));

        HeldWord carry = freed;
        foreach (uint ident in _shiftTemp)
            (carry, _absent[ident]) = (_absent[ident], carry);

        _spare.Enqueue(carry.Word, carry.DrawOrder);
        _stats.RejectWordsReclaimed++;
    }

    private HeldWord Draw()
    {
        return _spare.TryDequeue(out uint word, out ulong ordering)
            ? new HeldWord(word, ordering)
            : new HeldWord(_isaac.Next(), ++_draws);
    }

    private void Park(uint series)
    {
        if (_absent.ContainsKey(series))
            return;
        _absent.Add(series, Draw());
        _stats.TagsShelved++;
    }
}
