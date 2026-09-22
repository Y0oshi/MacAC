using System.Buffers;
using System.Numerics;

namespace MacAC.Client.Graphics;

public readonly struct LensPolyg
{
    public readonly Vector2[] Vertices;
    public readonly float LowerX, LowerY, UpperX, UpperY;

    public LensPolyg(Vector2[] verts)
    {
        Vertices = verts;
        if (verts is null || verts.Length < 3)
        {
            LowerX = LowerY = UpperX = UpperY = 0f;
            return;
        }
        float lowerX = float.MaxValue, lowerY = float.MaxValue, upperX = float.MinValue, upperY = float.MinValue;
        foreach (var v in verts)
        {
            if (v.X < lowerX) lowerX = v.X;
            if (v.X > upperX) upperX = v.X;
            if (v.Y < lowerY) lowerY = v.Y;
            if (v.Y > upperY) upperY = v.Y;
        }
        LowerX = lowerX; LowerY = lowerY; UpperX = upperX; UpperY = upperY;
    }

    public bool IsEmpty => Vertices is null || Vertices.Length < 3;
}

internal sealed class GatewayPolygVertVault
{
    private const int UpperKeptArrs = 4_096;
    private const int UpperKeptVerts = 65_536;
    private const int UpperKeptPolygVerts = 256;

    private sealed class Bin
    {
        public readonly List<Vector2[]> Buffers = new(4);
        public int Used;
    }

    private readonly Dictionary<int, Bin> _bins = [];
    private int _keptVerts;

    internal int AllocTally { get; private set; }
    internal int KeptArrTally { get; private set; }

    internal Vector2[] Rent(int vertTally)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(vertTally, 1);

        if (_bins.TryGetValue(vertTally, out Bin? bin)
            && bin.Used < bin.Buffers.Count)

            return bin.Buffers[bin.Used++];

        Vector2[] outcome = GC.AllocateUninitializedArray<Vector2>(vertTally);
        ++AllocTally;

        bool retain = vertTally <= UpperKeptPolygVerts
            && KeptArrTally < UpperKeptArrs
            && _keptVerts + vertTally <= UpperKeptVerts;
        if (!retain)
            return outcome;

        if (bin is null)
        {
            bin = new Bin();
            _bins.Add(vertTally, bin);
        }
        bin.Buffers.Add(outcome);
        bin.Used++;
        ++KeptArrTally;
        _keptVerts += vertTally;
        return outcome;
    }

    internal void RestartUsage()
    {
        foreach (Bin bin in _bins.Values)
            bin.Used = 0;
    }
}

public sealed class ChamberLens
{
    private readonly LensPolyg _wholeMonitorPolyg = new(
    [
        new Vector2(-1f, -1f),
        new Vector2(1f, -1f),
        new Vector2(1f, 1f),
        new Vector2(-1f, 1f),
    ]);

    public readonly List<LensPolyg> Polygons = [];

    private readonly Dictionary<int, int> _polygTagHeads = [];
    private readonly List<PolygTag> _polygTagDepot = [];
    private int _engagedPolygTagTally;
    public float MinX { get; private set; } = float.MaxValue;
    public float MinY { get; private set; } = float.MaxValue;
    public float MaxX { get; private set; } = float.MinValue;
    public float MaxY { get; private set; } = float.MinValue;

    public bool IsEmpty => Polygons.Count is 0;

    internal bool IsRetainable
    {
        get
        {
            if (Polygons.Capacity > 256
                || _polygTagHeads.EnsureCapacity(0) > 512
                || _polygTagDepot.Count > 512)

                return false;

            int keptCoordinateInts = 0;
            for (int idx = 0; idx < _polygTagDepot.Count; ++idx)
            {
                int len = _polygTagDepot[idx].Coordinates.Length;
                if (len > 256)
                    return false;
                keptCoordinateInts += len;
                if (keptCoordinateInts > 8192)
                    return false;
            }
            return true;
        }
    }

    public static ChamberLens WholeMonitor()
    {
        ChamberLens view = new ChamberLens();
        view.Add(view._wholeMonitorPolyg);
        return view;
    }

    public bool Add(LensPolyg polygon)
    {
        if (polygon.IsEmpty) return false;

        var tagOutcome = TryAppendCanonTag(polygon.Vertices);
        if (tagOutcome == CanonTagOutcome.Degenerate) return false;
        if (tagOutcome == CanonTagOutcome.Duplicate) return false;

        if (ContainedInExtant(polygon)) return false;

        Polygons.Add(polygon);
        if (polygon.LowerX < MinX) MinX = polygon.LowerX;
        if (polygon.LowerY < MinY) MinY = polygon.LowerY;
        if (polygon.UpperX > MaxX) MaxX = polygon.UpperX;
        if (polygon.UpperY > MaxY) MaxY = polygon.UpperY;
        return true;
    }

    internal void Reset()
    {
        Polygons.Clear();
        _polygTagHeads.Clear();
        _engagedPolygTagTally = 0;
        MinX = float.MaxValue;
        MinY = float.MaxValue;
        MaxX = float.MinValue;
        MaxY = float.MinValue;
    }

    internal void AssignWholeMonitor()
    {
        Reset();
        Add(_wholeMonitorPolyg);
    }

    private bool ContainedInExtant(in LensPolyg polygon)
    {
        const float eps = DedupGridNdc;
        for (int idx = 0; idx < Polygons.Count; ++idx)
        {
            LensPolyg e = Polygons[idx];
            if (polygon.LowerX < e.LowerX - eps || polygon.UpperX > e.UpperX + eps
                || polygon.LowerY < e.LowerY - eps || polygon.UpperY > e.UpperY + eps)
                continue;
            if (ContainsAllVerts(e.Vertices, polygon.Vertices, eps))
                return true;
        }
        return false;
    }

    private static bool ContainsAllVerts(Vector2[] convex, Vector2[] pts, float eps)
    {
        if (convex.Length < 3) return false;

        float area2 = 0f;
        for (int idx = 0; idx < convex.Length; ++idx)
        {
            Vector2 a = convex[idx];
            Vector2 b = convex[(idx + 1) % convex.Length];
            area2 += a.X * b.Y - b.X * a.Y;
        }
        float sign = area2 >= 0f ? 1f : -1f;

        for (int idx = 0; idx < convex.Length; ++idx)
        {
            Vector2 a = convex[idx];
            Vector2 b = convex[(idx + 1) % convex.Length];
            Vector2 ab = b - a;
            float length = ab.Length();
            if (length < 1e-9f) continue;
            foreach (var pt in pts)
            {
                float cross = sign * (ab.X * (pt.Y - a.Y) - ab.Y * (pt.X - a.X));
                if (cross < -eps * length)
                    return false;        // a vertex lies outside this edge by more than eps
            }
        }
        return true;
    }

    private const float DedupGridNdc = 1e-3f;

    private CanonTagOutcome TryAppendCanonTag(Vector2[]? verts)
    {
        if (verts is null || verts.Length < 3)
            return CanonTagOutcome.Degenerate;

        SnappedPt[]? rented = null;
        Span<SnappedPt> pts = verts.Length <= 32
            ? stackalloc SnappedPt[verts.Length]
            : (rented = ArrayPool<SnappedPt>.Shared.Rent(verts.Length)).AsSpan(0, verts.Length);

        try
        {
            int tally = 0;
            foreach (Vector2 vert in verts)
            {
                SnappedPt pt = new SnappedPt(
                    (int)MathF.Round(vert.X / DedupGridNdc),
                    (int)MathF.Round(vert.Y / DedupGridNdc));
                if (tally is 0 || pts[tally - 1] != pt)
                    pts[tally++] = pt;
            }
            if (tally >= 2 && pts[tally - 1] == pts[0])
                --tally;
            if (tally < 2)
                return CanonTagOutcome.Degenerate;

            var lo = pts[0];
            var hi = pts[0];
            for (int idx = 1; idx < tally; ++idx)
            {
                var pt = pts[idx];
                if (pt.X < lo.X || (pt.X == lo.X && pt.Y < lo.Y)) lo = pt;
                if (pt.X > hi.X || (pt.X == hi.X && pt.Y > hi.Y)) hi = pt;
            }

            bool removed = true;
            while (removed && tally >= 3)
            {
                removed = false;
                for (int idx = 0; idx < tally && tally >= 3; ++idx)
                {
                    var earlier = pts[(idx + tally - 1) % tally];
                    var latest = pts[idx];
                    var upcoming = pts[(idx + 1) % tally];
                    long cross = (long)(latest.X - earlier.X) * (upcoming.Y - latest.Y)
                               - (long)(latest.Y - earlier.Y) * (upcoming.X - latest.X);
                    if (cross is not 0)
                        continue;

                    pts.Slice(idx + 1, tally - idx - 1).CopyTo(pts[idx..]);
                    --tally;
                    removed = true;
                    --idx;
                }
            }

            if (tally < 3)
            {
                if (lo == hi)
                    return CanonTagOutcome.Degenerate;
                Span<SnappedPt> segment = [lo, hi];
                return AppendCanonTag(PolygTagFlavor.Line, segment, begin: 0);
            }

            int finest = 0;
            for (int begin = 1; begin < tally; ++begin)
                if (SpinLess(pts, begin, finest, tally)) finest = begin;

            return AppendCanonTag(PolygTagFlavor.Polygon, pts[..tally], finest);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<SnappedPt>.Shared.Return(rented);
        }
    }

    private CanonTagOutcome AppendCanonTag(
        PolygTagFlavor sort,
        ReadOnlySpan<SnappedPt> pts,
        int begin)
    {
        int digest = CalculateDigest(sort, pts, begin);
        if (_polygTagHeads.TryGetValue(digest, out int tagOrdinal))
        {
            while (tagOrdinal >= 0)
            {
                PolygTag extant = _polygTagDepot[tagOrdinal];
                if (extant.Equals(sort, pts, begin))
                    return CanonTagOutcome.Duplicate;
                tagOrdinal = extant.Next;
            }
        }

        int coordinateTally = pts.Length * 2;
        int depotOrdinal = SeekOrBuildCoordinateDepot(coordinateTally);
        int[] coordinates = _polygTagDepot[depotOrdinal].Coordinates;
        for (int idx = 0; idx < pts.Length; ++idx)
        {
            var pt = pts[(begin + idx) % pts.Length];
            coordinates[idx * 2] = pt.X;
            coordinates[idx * 2 + 1] = pt.Y;
        }
        int upcoming = _polygTagHeads.GetValueOrDefault(digest, -1);
        _polygTagDepot[depotOrdinal] = new PolygTag(sort, coordinates, upcoming);
        _polygTagHeads[digest] = depotOrdinal;
        ++_engagedPolygTagTally;
        return CanonTagOutcome.Added;
    }

    private int SeekOrBuildCoordinateDepot(int coordinateTally)
    {
        int depotOrdinal = _engagedPolygTagTally;
        for (int idx = depotOrdinal; idx < _polygTagDepot.Count; ++idx)
        {
            if (_polygTagDepot[idx].Coordinates.Length != coordinateTally)
                continue;
            if (idx != depotOrdinal)
                (_polygTagDepot[depotOrdinal], _polygTagDepot[idx]) =
                    (_polygTagDepot[idx], _polygTagDepot[depotOrdinal]);
            return depotOrdinal;
        }

        _polygTagDepot.Add(new PolygTag(
            PolygTagFlavor.Polygon,
            new int[coordinateTally],
            -1));
        int addedOrdinal = _polygTagDepot.Count - 1;
        if (addedOrdinal != depotOrdinal)
            (_polygTagDepot[depotOrdinal], _polygTagDepot[addedOrdinal]) =
                (_polygTagDepot[addedOrdinal], _polygTagDepot[depotOrdinal]);
        return depotOrdinal;
    }

    private static int CalculateDigest(
        PolygTagFlavor sort,
        ReadOnlySpan<SnappedPt> pts,
        int begin)
    {
        unchecked
        {
            uint digest = 2166136261u;
            digest = (digest ^ (byte)sort) * 16777619u;
            digest = (digest ^ (uint)pts.Length) * 16777619u;
            for (int idx = 0; idx < pts.Length; ++idx)
            {
                var pt = pts[(begin + idx) % pts.Length];
                digest = (digest ^ (uint)pt.X) * 16777619u;
                digest = (digest ^ (uint)pt.Y) * 16777619u;
            }
            return (int)digest;
        }
    }

    private static bool SpinLess(
        ReadOnlySpan<SnappedPt> pts,
        int a,
        int b,
        int tally)
    {
        for (int idx = 0; idx < tally; ++idx)
        {
            var left = pts[(a + idx) % tally];
            var right = pts[(b + idx) % tally];
            if (left.X != right.X) return left.X < right.X;
            if (left.Y != right.Y) return left.Y < right.Y;
        }
        return false;
    }

    private readonly record struct SnappedPt(int X, int Y);

    private readonly record struct PolygTag(PolygTagFlavor Kind, int[] Coordinates, int Next)
    {
        public bool Equals(PolygTagFlavor sort, ReadOnlySpan<SnappedPt> pts, int begin)
        {
            if (Kind != sort || Coordinates.Length != pts.Length * 2)
                return false;
            for (int idx = 0; idx < pts.Length; ++idx)
            {
                var pt = pts[(begin + idx) % pts.Length];
                if (Coordinates[idx * 2] != pt.X || Coordinates[idx * 2 + 1] != pt.Y)
                    return false;
            }
            return true;
        }
    }

    private enum PolygTagFlavor : byte { Polygon, Line }
    private enum CanonTagOutcome : byte { Degenerate, Duplicate, Added }
}
