using System.Buffers;
using System.Numerics;

namespace MacAC.Client.Graphics;

public static class PortalMirror
{
    internal ref struct ClipPolygonLease
    {
        private readonly ArrayPool<Vector4>? _reservoir;
        private Vector4[]? _lead;
        private Vector4[]? _second;
        private Vector4[]? _outcome;
        private readonly int _tally;
        private bool _destroyed;

        internal ClipPolygonLease(
            ArrayPool<Vector4>? reservoir,
            Vector4[]? lead,
            Vector4[]? second,
            Vector4[]? outcome,
            int tally)
        {
            _reservoir = reservoir;
            _lead = lead;
            _second = second;
            _outcome = outcome;
            _tally = tally;
            _destroyed = false;
        }

        public int Count
        {
            get
            {
                HurlIfDestroyed();
                return _tally;
            }
        }

        public ReadOnlySpan<Vector4> Span
        {
            get
            {
                HurlIfDestroyed();
                return _outcome is null
                    ? ReadOnlySpan<Vector4>.Empty
                    : _outcome.AsSpan(0, _tally);
            }
        }

        public void Dispose()
        {
            if (_destroyed)
                return;

            _destroyed = true;
            var reservoir = _reservoir;
            if (_lead is not null)
                reservoir!.Return(_lead);
            if (_second is not null)
                reservoir!.Return(_second);
            _lead = null;
            _second = null;
            _outcome = null;
        }

        private readonly void HurlIfDestroyed()
        {
            if (_destroyed)
                throw new ObjectDisposedException(nameof(ClipPolygonLease));
        }
    }

    public static Vector2[] ProjectToNdc(IReadOnlyList<Vector3> ownPoly, Matrix4x4 chamberToRealm, Matrix4x4 lensProj)
    {
        return ProjectToNdc(ownPoly, chamberToRealm, lensProj, ArrayPool<Vector4>.Shared);
    }

    public static Vector4[] ProjectToClip(IReadOnlyList<Vector3> ownPoly, Matrix4x4 chamberToRealm, Matrix4x4 lensProj)
    {
        using var tenancy = ProjectToClipTenancy(ownPoly, chamberToRealm, lensProj);
        return tenancy.Count < 3 ? [] : tenancy.Span.ToArray();
    }

    public static Vector2[] ClipToRegion(IReadOnlyList<Vector4> subjectClip, IReadOnlyList<Vector2> zoneCcwNdc)
    {
        if (subjectClip is null || zoneCcwNdc is null || subjectClip.Count < 3 || zoneCcwNdc.Count < 3)
            return [];

        if (subjectClip is Vector4[] arr)
            return ClipToZone(arr.AsSpan(), zoneCcwNdc);

        Vector4[] rented = ArrayPool<Vector4>.Shared.Rent(subjectClip.Count);
        try
        {
            for (int idx = 0; idx < subjectClip.Count; ++idx)
                rented[idx] = subjectClip[idx];
            return ClipToZone(rented.AsSpan(0, subjectClip.Count), zoneCcwNdc);
        }
        finally
        {
            ArrayPool<Vector4>.Shared.Return(rented);
        }
    }

    internal static Vector2[] ProjectToNdc(
        IReadOnlyList<Vector3> ownPoly,
        Matrix4x4 chamberToRealm,
        Matrix4x4 lensProj,
        ArrayPool<Vector4> vectorReservoir)
    {
        if (ownPoly is null || ownPoly.Count < 3) return [];
        ArgumentNullException.ThrowIfNull(vectorReservoir);

        Matrix4x4 m = chamberToRealm * lensProj;

        int cap = checked(ownPoly.Count + 5);
        Vector4[] lead = vectorReservoir.Rent(cap);
        Vector4[]? second = null;

        try
        {
            second = vectorReservoir.Rent(cap);
            int latestTally = ownPoly.Count;
            for (int idx = 0; idx < latestTally; ++idx)
                lead[idx] = Vector4.Transform(new Vector4(ownPoly[idx], 1f), m);

            Vector4[] latest = lead;
            Vector4[] product = second;
            ReadOnlySpan<HomogeneousFacet> planes =
            [
                HomogeneousFacet.EyeMinW,
                HomogeneousFacet.Left,
                HomogeneousFacet.Right,
                HomogeneousFacet.Bottom,
                HomogeneousFacet.Top,
            ];
            foreach (HomogeneousFacet plane in planes)
            {
                latestTally = ClipHomogeneousPlane(
                    latest.AsSpan(0, latestTally), product, plane);
                if (latestTally < 3)
                    return [];
                (latest, product) = (product, latest);
            }

            Vector2[] ndc = new Vector2[latestTally];
            for (int idx = 0; idx < latestTally; ++idx)
            {
                float w = latest[idx].W;
                ndc[idx] = new Vector2(latest[idx].X / w, latest[idx].Y / w);
            }
            return ndc;
        }
        finally
        {
            vectorReservoir.Return(lead);
            if (second is not null)
                vectorReservoir.Return(second);
        }
    }

    internal static ClipPolygonLease ProjectToClipTenancy(
        IReadOnlyList<Vector3> ownPoly,
        Matrix4x4 chamberToRealm,
        Matrix4x4 lensProj)
    {
        return ProjectToClipTenancy(
                ownPoly,
                chamberToRealm,
                lensProj,
                ArrayPool<Vector4>.Shared);
    }

    internal static ClipPolygonLease ProjectToClipTenancy(
        IReadOnlyList<Vector3> ownPoly,
        Matrix4x4 chamberToRealm,
        Matrix4x4 lensProj,
        ArrayPool<Vector4> vectorReservoir)
    {
        ArgumentNullException.ThrowIfNull(vectorReservoir);
        if (ownPoly is null || ownPoly.Count < 3)
            return new ClipPolygonLease(null, null, null, null, 0);

        Matrix4x4 m = chamberToRealm * lensProj;
        Vector4[] transformed = vectorReservoir.Rent(ownPoly.Count);
        Vector4[]? clipped = null;
        bool success = false;
        try
        {
            clipped = vectorReservoir.Rent(checked(ownPoly.Count + 1));
            bool anyBehind = false;
            for (int idx = 0; idx < ownPoly.Count; ++idx)
            {
                Vector4 vert = Vector4.Transform(new Vector4(ownPoly[idx], 1f), m);
                if (vert.W < 0f) anyBehind = true;
                transformed[idx] = vert;
            }

            ReadOnlySpan<Vector4> outcome = transformed.AsSpan(0, ownPoly.Count);
            if (anyBehind)
            {
                int tally = ClipHomogeneousPlane(outcome, clipped, HomogeneousFacet.EyeZero);
                if (tally < 3)
                {
                    success = true;
                    return new ClipPolygonLease(
                        vectorReservoir,
                        transformed,
                        clipped,
                        null,
                        0);
                }
                outcome = clipped.AsSpan(0, tally);
            }

            Vector4[] outcomeArr = anyBehind ? clipped : transformed;
            success = true;
            return new ClipPolygonLease(
                vectorReservoir,
                transformed,
                clipped,
                outcomeArr,
                outcome.Length);
        }
        finally
        {
            if (!success)
            {
                vectorReservoir.Return(transformed);
                if (clipped is not null)
                    vectorReservoir.Return(clipped);
            }
        }
    }

    internal static Vector2[] ClipToZone(
        ReadOnlySpan<Vector4> subjectClip,
        IReadOnlyList<Vector2> zoneCcwNdc)
        => ClipToZoneCore(subjectClip, zoneCcwNdc, vertVault: null);

    internal static Vector2[] ClipToZone(
        ReadOnlySpan<Vector4> subjectClip,
        IReadOnlyList<Vector2> zoneCcwNdc,
        GatewayPolygVertVault vertVault)
    {
        return ClipToZoneCore(
                subjectClip,
                zoneCcwNdc,
                vertVault,
                ArrayPool<Vector4>.Shared,
                ArrayPool<Vector2>.Shared);
    }

    internal static Vector2[] ClipToZone(
        ReadOnlySpan<Vector4> subjectClip,
        IReadOnlyList<Vector2> zoneCcwNdc,
        GatewayPolygVertVault vertVault,
        ArrayPool<Vector4> vector4Reservoir)
    {
        return ClipToZoneCore(
                subjectClip,
                zoneCcwNdc,
                vertVault,
                vector4Reservoir,
                ArrayPool<Vector2>.Shared);
    }

    private static Vector2[] ClipToZoneCore(
        ReadOnlySpan<Vector4> subjectClip,
        IReadOnlyList<Vector2> zoneCcwNdc,
        GatewayPolygVertVault? vertVault,
        ArrayPool<Vector4>? vector4Reservoir = null,
        ArrayPool<Vector2>? vector2Reservoir = null)
    {
        if (subjectClip.Length < 3 || zoneCcwNdc is null || zoneCcwNdc.Count < 3)
            return [];
        vector4Reservoir ??= ArrayPool<Vector4>.Shared;
        vector2Reservoir ??= ArrayPool<Vector2>.Shared;

        int zoneTally = zoneCcwNdc.Count;
        int cap = checked(subjectClip.Length + zoneTally);
        Vector4[] lead = vector4Reservoir.Rent(cap);
        Vector4[]? second = null;
        Vector2[]? ndcTemp = null;

        try
        {
            second = vector4Reservoir.Rent(cap);
            int latestTally = subjectClip.Length;
            subjectClip.CopyTo(lead);

            Vector4[] latest = lead;
            Vector4[] product = second;
            for (int rim = 0; rim < zoneTally; ++rim)
            {
                if (latestTally < 3)
                    return [];
                int productTally = ClipHomogeneousRim(
                    latest.AsSpan(0, latestTally),
                    product,
                    zoneCcwNdc[rim],
                    zoneCcwNdc[(rim + 1) % zoneTally]);
                (latest, product) = (product, latest);
                latestTally = productTally;
            }
            if (latestTally < 3)
                return [];

            ndcTemp = vector2Reservoir.Rent(latestTally);
            var ndc = ndcTemp.AsSpan(0, latestTally);
            for (int idx = 0; idx < latestTally; ++idx)
            {
                float w = latest[idx].W;
                Vector2 vert = new Vector2(latest[idx].X / w, latest[idx].Y / w);
                if (!float.IsFinite(vert.X) || !float.IsFinite(vert.Y))
                    return [];
                ndc[idx] = vert;
            }

            int mergedTally = CombineSubPixelVerts(ndc);
            if (mergedTally < 3)
                return [];
            Vector2[] merged = vertVault?.Rent(mergedTally)
                ?? GC.AllocateUninitializedArray<Vector2>(mergedTally);
            ndc[..mergedTally].CopyTo(merged);

            SecureCcw(merged);
            return merged;
        }
        finally
        {
            vector4Reservoir.Return(lead);
            if (second is not null)
                vector4Reservoir.Return(second);
            if (ndcTemp is not null)
                vector2Reservoir.Return(ndcTemp);
        }
    }

    private const float VertCombineEpsilonNdc = 2f / 1080f;

    private static int CombineSubPixelVerts(Span<Vector2> poly)
    {
        if (poly.Length < 3) return poly.Length;
        int kept = 0;
        for (int idx = 0; idx < poly.Length; ++idx)
        {
            Vector2 vert = poly[idx];
            if (kept > 0)
            {
                Vector2 earlier = poly[kept - 1];
                if (MathF.Abs(vert.X - earlier.X) <= VertCombineEpsilonNdc
                    && MathF.Abs(vert.Y - earlier.Y) <= VertCombineEpsilonNdc)
                    continue;
            }
            poly[kept++] = vert;
        }
        while (kept >= 2)
        {
            Vector2 lead = poly[0];
            Vector2 last = poly[kept - 1];
            if (MathF.Abs(lead.X - last.X) <= VertCombineEpsilonNdc
                && MathF.Abs(lead.Y - last.Y) <= VertCombineEpsilonNdc)
                --kept;
            else
                break;
        }
        return kept;
    }

    private static int ClipHomogeneousRim(
        ReadOnlySpan<Vector4> polyg,
        Span<Vector4> outcome,
        Vector2 a,
        Vector2 b)
    {
        int productTally = 0;
        float exc = b.X - a.X, ey = b.Y - a.Y;
        for (int idx = 0; idx < polyg.Length; ++idx)
        {
            Vector4 cur = polyg[idx];
            Vector4 earlier = polyg[(idx + polyg.Length - 1) % polyg.Length];
            float dCur = exc * (cur.Y - cur.W * a.Y) - ey * (cur.X - cur.W * a.X);
            float dEarlier = exc * (earlier.Y - earlier.W * a.Y) - ey * (earlier.X - earlier.W * a.X);
            bool curIn = dCur >= 0f;
            bool earlierIn = dEarlier >= 0f;

            if (curIn)
            {
                if (!earlierIn) outcome[productTally++] = Lerp(earlier, cur, dEarlier, dCur);
                outcome[productTally++] = cur;
            }
            else if (earlierIn)
            {
                outcome[productTally++] = Lerp(earlier, cur, dEarlier, dCur);
            }
        }
        return productTally;
    }

    private static void SecureCcw(Vector2[] poly)
    {
        float area2 = 0f;
        for (int idx = 0; idx < poly.Length; ++idx)
        {
            Vector2 p = poly[idx]; Vector2 q = poly[(idx + 1) % poly.Length];
            area2 += p.X * q.Y - q.X * p.Y;
        }
        if (area2 < 0f) System.Array.Reverse(poly);
    }

    private const float LowerW = 0.05f;

    private static int ClipHomogeneousPlane(
        ReadOnlySpan<Vector4> polyg,
        Span<Vector4> outcome,
        HomogeneousFacet plane)
    {
        int productTally = 0;
        for (int idx = 0; idx < polyg.Length; ++idx)
        {
            Vector4 cur = polyg[idx];
            Vector4 earlier = polyg[(idx + polyg.Length - 1) % polyg.Length];
            float dCur = PlaneGap(cur, plane);
            float dEarlier = PlaneGap(earlier, plane);
            bool curIn = dCur >= 0f;
            bool earlierIn = dEarlier >= 0f;

            if (curIn)
            {
                if (!earlierIn) outcome[productTally++] = Lerp(earlier, cur, dEarlier, dCur);
                outcome[productTally++] = cur;
            }
            else if (earlierIn)
            {
                outcome[productTally++] = Lerp(earlier, cur, dEarlier, dCur);
            }
        }
        return productTally;
    }

    private static float PlaneGap(in Vector4 vert, HomogeneousFacet plane)
    {
        return plane switch
        {
            HomogeneousFacet.EyeMinW => vert.W - LowerW,
            HomogeneousFacet.EyeZero => vert.W,
            HomogeneousFacet.Left => vert.W + vert.X,
            HomogeneousFacet.Right => vert.W - vert.X,
            HomogeneousFacet.Bottom => vert.W + vert.Y,
            HomogeneousFacet.Top => vert.W - vert.Y,
            _ => throw new System.ArgumentOutOfRangeException(nameof(plane)),
        };
    }

    private enum HomogeneousFacet : byte
    {
        EyeMinW,
        EyeZero,
        Left,
        Right,
        Bottom,
        Top,
    }

    private static Vector4 Lerp(Vector4 p, Vector4 q, float dp, float dq)
    {
        float t = dp / (dp - dq);
        return p + t * (q - p);
    }
}
