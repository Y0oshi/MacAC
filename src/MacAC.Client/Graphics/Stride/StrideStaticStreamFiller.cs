using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Graphics.Stride;

internal sealed class StrideStaticStreamFiller
{
    private readonly RealmPaintRouter _router;
    private readonly List<RealmPaintRouter.StrideClassifiedBatch> _lotTemp = [];
    private readonly List<RealmPaintRouter.StrideClassifiedPickingPart> _pickTemp = [];
    private readonly List<ChamberLot> _chamberLotTemp = [];

    private readonly record struct ChamberLot(
        RealmPaintRouter.StrideClassifiedBatch Batch,
        StrollPaintJuncture Stage,
        uint LocalEntityId);

    internal StrideStaticStreamFiller(RealmPaintRouter dispatcher)
    {
        _router = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    internal void FillChamber(
        SequencedPaintFlow flow,
        StrollPaintJuncture juncture,
        uint chamberIdent,
        ReadOnlySpan<RenderMirrorRecord> records,
        uint tupleLbIdent,
        Vector3 camRealmLocus,
        Matrix4x4 lensProj,
        IStrideLookInViewSource? views = null,
        int lensCourseOrdinal = -1,
        List<RealmPaintRouter.StrideClassifiedBatch>? alphaSubmissions = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        for (int idx = 0; idx < records.Length; idx++)
        {
            ClassifyAndAffix(
                flow, juncture, chamberIdent, in records[idx], tupleLbIdent,
                camRealmLocus, lensProj,
                onlineDynamic: false, views, lensCourseOrdinal, alphaSubmissions);
        }
    }

    internal void FillStructureShell(
        SequencedPaintFlow flow,
        uint chamberIdent,
        in RenderMirrorRecord capture,
        uint tupleLbIdent,
        Vector3 camRealmLocus,
        Matrix4x4 lensProj,
        in StrideBuildingPicking pick,
        Matrix4x4 pieceZeroXform,
        List<RealmPaintRouter.StrideClassifiedBatch> alphaSubmissions)
    {
        ClassifyAndAffix(
            flow,
            StrollPaintJuncture.BuildingShell,
            chamberIdent,
            in capture,
            tupleLbIdent,
            camRealmLocus,
            lensProj,
            alphaSubmissions: alphaSubmissions,
            structurePick: pick,
            structurePieceXform: pieceZeroXform);
    }

    internal void FillExteriorStatics(
        SequencedPaintFlow flow,
        uint chamberIdent,
        ReadOnlySpan<RenderMirrorRecord> records,
        uint tupleLbIdent,
        Vector3 camRealmLocus,
        Matrix4x4 lensProj,
        IStrideLookInViewSource? views = null,
        int lensCourseOrdinal = -1,
        ISet<RenderMirrorId>? drawnOnce = null,
        List<RealmPaintRouter.StrideClassifiedBatch>? alphaSubmissions = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        for (int idx = 0; idx < records.Length; idx++)
        {
            if (drawnOnce is not null && !drawnOnce.Add(records[idx].Id))
                continue;
            ClassifyAndAffix(
                flow, StrollPaintJuncture.OutdoorStatic, chamberIdent, in records[idx],
                tupleLbIdent, camRealmLocus, lensProj,
                onlineDynamic: false, views, lensCourseOrdinal,
                alphaSubmissions: alphaSubmissions);
        }
    }

    internal void FillChamberDynamics(
        SequencedPaintFlow flow,
        uint chamberIdent,
        ReadOnlySpan<RenderMirrorRecord> records,
        uint tupleLbIdent,
        Vector3 camRealmLocus,
        Matrix4x4 lensProj,
        IStrideLookInViewSource? gazeInViews = null,
        int gazeInCourseOrdinal = -1,
        ISet<RenderMirrorId>? drawnOnce = null,
        List<RealmPaintRouter.StrideClassifiedBatch>? alphaSubmissions = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        for (int idx = 0; idx < records.Length; idx++)
        {
            if (drawnOnce is not null && !drawnOnce.Add(records[idx].Id))
                continue;
            ClassifyAndAffix(
                flow,
                StrollPaintJuncture.Dynamic,
                chamberIdent,
                in records[idx],
                tupleLbIdent,
                camRealmLocus,
                lensProj,
                onlineDynamic: true,
                gazeInViews,
                gazeInCourseOrdinal,
                alphaSubmissions: alphaSubmissions);
        }
    }

    internal void FillChamberObjects(
        SequencedPaintFlow flow,
        StrollPaintJuncture staticJuncture,
        uint chamberIdent,
        ReadOnlySpan<RenderMirrorRecord> records,
        uint tupleLbIdent,
        Vector3 camRealmLocus,
        Matrix4x4 lensProj,
        IStrideLookInViewSource? views,
        int lensCourseOrdinal,
        List<RealmPaintRouter.StrideClassifiedBatch> alphaSubmissions)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(alphaSubmissions);
        _chamberLotTemp.Clear();

        for (int captureOrdinal = 0; captureOrdinal < records.Length; captureOrdinal++)
        {
            ref readonly RenderMirrorRecord capture = ref records[captureOrdinal];
            bool onlineDynamic = capture.ProjectionClass is RenderMirrorClass.LiveDynamicRoot
                or RenderMirrorClass.EquippedChild;
            StrollPaintJuncture juncture = onlineDynamic ? StrollPaintJuncture.Dynamic : staticJuncture;
            _lotTemp.Clear();
            _pickTemp.Clear();
            _router.ClassifyActorForStroll(
                in capture,
                tupleLbIdent,
                _lotTemp,
                _pickTemp,
                onlineDynamic,
                views,
                lensCourseOrdinal,
                chamberIdent);

            for (int lotOrdinal = 0; lotOrdinal < _lotTemp.Count; lotOrdinal++)
            {
                RealmPaintRouter.StrideClassifiedBatch lot = _lotTemp[lotOrdinal];
                var realmOrderMiddle = Vector3.Transform(lot.LocalSortCenter, lot.Transform);
                lot = lot with
                {
                    SortDistanceSq = Vector3.DistanceSquared(
                        realmOrderMiddle, camRealmLocus),
                };
                _chamberLotTemp.Add(new ChamberLot(
                    lot,
                    juncture,
                    capture.Source.LocalEntityId));
            }

            for (int pickOrdinal = 0; pickOrdinal < _pickTemp.Count; pickOrdinal++)
            {
                RealmPaintRouter.StrideClassifiedPickingPart piece =
                    _pickTemp[pickOrdinal];
                _router.BroadcastStrollPickPiece(in piece);
            }
        }

        StableOrderChamberLots(_chamberLotTemp);
        for (int idx = 0; idx < _chamberLotTemp.Count; idx++)
        {
            ChamberLot gear = _chamberLotTemp[idx];
            RealmPaintRouter.StrideClassifiedBatch lot = gear.Batch;
            if (lot.IsOpaque)
            {
                flow.Tack(new OrderedDrawDirective(
                    lot.Key, lot.Transform, gear.Stage, chamberIdent, lot.ClipSlot,
                    lot.Lights, lot.IndoorFlag, lot.Alpha,
                    lot.SelectionLighting, lot.DetailCategory));
            }
            else
            {
                alphaSubmissions.Add(lot);
            }
        }
    }

    private static void StableOrderChamberLots(List<ChamberLot> lots)
    {
        for (int idx = 1; idx < lots.Count; idx++)
        {
            ChamberLot val = lots[idx];
            int insertion = idx;
            while (insertion > 0
                && val.Batch.SortDistanceSq > lots[insertion - 1].Batch.SortDistanceSq)
            {
                lots[insertion] = lots[insertion - 1];
                insertion--;
            }
            lots[insertion] = val;
        }
    }

    private void ClassifyAndAffix(
        SequencedPaintFlow flow,
        StrollPaintJuncture juncture,
        uint chamberIdent,
        in RenderMirrorRecord capture,
        uint tupleLbIdent,
        Vector3 camRealmLocus,
        Matrix4x4 lensProj,
        bool onlineDynamic = false,
        IStrideLookInViewSource? gazeInViews = null,
        int gazeInCourseOrdinal = -1,
        List<RealmPaintRouter.StrideClassifiedBatch>? alphaSubmissions = null,
        StrideBuildingPicking? structurePick = null,
        Matrix4x4 structurePieceXform = default)
    {
        _lotTemp.Clear();
        _pickTemp.Clear();
        _router.ClassifyActorForStroll(
            in capture,
            tupleLbIdent,
            _lotTemp,
            _pickTemp,
            onlineDynamic,
            gazeInViews,
            gazeInCourseOrdinal,
            chamberIdent,
            structurePick: structurePick,
            structurePieceXform: structurePieceXform);

        for (int idx = 0; idx < _lotTemp.Count; idx++)
        {
            RealmPaintRouter.StrideClassifiedBatch lot = _lotTemp[idx];
            if (lot.IsOpaque)
            {
                flow.Tack(new OrderedDrawDirective(
                    lot.Key, lot.Transform, juncture, chamberIdent, lot.ClipSlot,
                    lot.Lights, lot.IndoorFlag, lot.Alpha,
                    lot.SelectionLighting, lot.DetailCategory));
            }
            else
            {
                if (alphaSubmissions is null)
                {
                    _router.SubmitWalkAlphaInstance(
                        in lot, lensProj);
                }
                else
                {
                    alphaSubmissions.Add(lot);
                }
            }
        }

        for (int idx = 0; idx < _pickTemp.Count; idx++)
        {
            RealmPaintRouter.StrideClassifiedPickingPart piece = _pickTemp[idx];
            _router.BroadcastStrollPickPiece(in piece);
        }
    }
}
