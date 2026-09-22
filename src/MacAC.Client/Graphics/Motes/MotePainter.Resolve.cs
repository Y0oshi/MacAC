using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Geometry;
using RuntimeParticleEmitter = MacAC.Mechanics.Effects.MoteSpout;

namespace MacAC.Client.Graphics;

public sealed unsafe partial class MotePainter
{
    internal static ReadiedChamberAlphaActs LocateReadiedChamberAlphaActs(
        CanonAlphaMeshDecision decision,
        ref int keptClipTally,
        ref int keptAlphaTally)
    {
        bool reqsRetention = decision.Action is CanonAlphaMeshAction.Append
            or CanonAlphaMeshAction.AppendClipAndImmediate;
        bool retain = false;
        if (reqsRetention)
        {
            if (decision.List == CanonAlphaList.Clip)
            {
                if (keptClipTally < CanonAlphaFifo.RosterCap)
                {
                    ++keptClipTally;
                    retain = true;
                }
            }
            else if (keptAlphaTally < CanonAlphaFifo.RosterCap)
            {
                ++keptAlphaTally;
                retain = true;
            }
        }

        return new ReadiedChamberAlphaActs(
            retain,
            decision.Action is CanonAlphaMeshAction.Immediate
                or CanonAlphaMeshAction.AppendClipAndImmediate);
    }

    private SeeThroughKind LocateTriMeshBlend(ThingRasterizeLot lot)
    {
        uint canvasIdent = lot.Key.SurfaceId;
        if (canvasIdent is 0 || _datFiles is null)
            return lot.IsAdditive ? SeeThroughKind.Additive : SeeThroughKind.AlphaBlend;
        if (_triMeshBlendByCanvas.TryGetValue(canvasIdent, out SeeThroughKind blend))
            return blend;

        blend = CanonMoteBlendPicker.Resolve(
            canvasIdent,
            lot.IsAdditive,
            ident => _datFiles.Get<Skin>(ident),
            Console.Error.WriteLine);
        _triMeshBlendByCanvas[canvasIdent] = blend;
        return blend;
    }

    private CanonMoteGeometryKind LocateGeoSort(uint gfxObjRefIdent)
    {
        if (_geoSortByGfxObjRef.TryGetValue(gfxObjRefIdent, out CanonMoteGeometryKind sort))
            return sort;

        sort = CanonMoteGeometryClassifier.Classify(
            LocateLeadDowngradeManner(gfxObjRefIdent));
        _geoSortByGfxObjRef[gfxObjRefIdent] = sort;
        return sort;
    }

    private uint? LocateLeadDowngradeManner(uint gfxObjRefIdent)
    {
        if (_leadDowngradeMannerByGfxObjRef.TryGetValue(gfxObjRefIdent, out uint? manner))
            return manner;

        try
        {
            if (_datFiles?.Get<PartMesh>(gfxObjRefIdent) is { } gfx
                && gfx.Bits.HasFlag(PartMeshBits.HasDIDDegrade)
                && gfx.LodTableId is not 0
                && _datFiles.Get<LodTable>(gfx.LodTableId) is { Levels.Count: > 0 } downgrade)

                manner = downgrade.Levels[0].DegradeMode;
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine(
                $"[particle-geometry] Could not decode GfxObj 0x{gfxObjRefIdent:X8} degrade metadata: {exc.Message}");
        }

        _leadDowngradeMannerByGfxObjRef[gfxObjRefIdent] = manner;
        return manner;
    }

    private MoteGfxInfo LocateMoteGfxDetails(RuntimeParticleEmitter spout)
    {
        if (_textures is null)
            return MoteGfxInfo.Default;
        if (_moteGfxDetailsBySpout.TryGetValue(spout.Handle, out MoteGfxInfo settled))
            return settled;

        EmitterSpec descriptor = spout.Desc;

        if (descriptor.TextureCanvasIdent is not 0)
        {
            settled = MoteGfxInfo.Billboard(
                _textures.ObtainMoteTexture(spout.Handle, descriptor.TextureCanvasIdent),
                Vector2.One,
                Vector3.Zero,
                Vector3.Zero,
                additive: (descriptor.Flags & EmitterBits.Additive) != 0,
                hasMatl: false,
                canvasIdent: descriptor.TextureCanvasIdent);
            _moteGfxDetailsBySpout.Add(spout.Handle, settled);
            return settled;
        }

        uint gfxObjRefIdent = descriptor.HwGfxObjId is not 0 ? descriptor.HwGfxObjId : descriptor.GfxObjId;
        if (gfxObjRefIdent is 0 || _datFiles is null)
            return MoteGfxInfo.Default;

        if (!_moteGfxDetailsByGfxObjRef.TryGetValue(gfxObjRefIdent, out var details))
        {
            details = ScanMoteGfxDetails(gfxObjRefIdent);
            _moteGfxDetailsByGfxObjRef[gfxObjRefIdent] = details;
        }

        settled = details;
        if (details.SurfaceId is not 0)
        {
            settled = details with
            {
                TextureSlot = _textures.ObtainMoteTexture(
                    spout.Handle,
                    details.SurfaceId),
            };
        }
        _moteGfxDetailsBySpout.Add(spout.Handle, settled);
        return settled;
    }
}
