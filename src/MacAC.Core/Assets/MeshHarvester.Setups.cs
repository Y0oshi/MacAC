using System.Numerics;
using MacAC.Dat;
using Microsoft.Extensions.Logging;
using Sphere =  MacAC.Dat.Orb;

namespace MacAC.Assets;

/// <summary>Composite objects: Setups, furnished EnvCells and their particle scripts.</summary>
public sealed partial class MeshHarvester
{
    private const int UpperPieceZDepth = 50;

    private HarvestedMesh? HarvestRig(ulong ident, RigSpec rig, CancellationToken token)
    {
        var pieces = new List<(ulong GfxObjId, Matrix4x4 Transform)>();
        var reach = new Extent();
        CollectPieces((uint)(ident & 0xFFFFFFFFu), Matrix4x4.Identity, pieces, ref reach, token, 0);

        List<QueuedEmitter> spouts = new List<QueuedEmitter>();
        if (rig.DefaultEffectId is not 0)
            CollectSpouts(rig.DefaultEffectId, spouts, token);

        return new HarvestedMesh
        {
            ObjectId = ident,
            IsSetup = true,
            SetupParts = pieces,
            ParticleEmitters = spouts,
            BoundingBox = reach.Box,
            SelectionSphere = rig.SelectionOrb,
        };
    }

    private HarvestedMesh? HarvestEnvironChamber(ulong ident, RoomCell chamber, CancellationToken token)
    {
        var pieces = new List<(ulong GfxObjId, Matrix4x4 Transform)>();
        var reach = new Extent();
        Matrix4x4 intoChamber = InvOf(Posture(chamber.Position));

        // Furniture is authored in world space; bring it into the cell's frame
        List<QueuedEmitter> spouts = new List<QueuedEmitter>();
        foreach (PlacedObject stab in chamber.StaticObjects)
        {
            Matrix4x4 local = Posture(stab.Pose) * intoChamber;
            CollectPieces(stab.Id, local, pieces, ref reach, token, 0);

            if ((stab.Id & 0xFF000000u) != 0x02000000u || !_datFiles.Portal.TryGet<RigSpec>(stab.Id, out var furniture))
                continue;
            if (furniture.DefaultEffectId is 0)
                continue;

            List<QueuedEmitter> own = new List<QueuedEmitter>();
            CollectSpouts(furniture.DefaultEffectId, own, token);
            foreach (QueuedEmitter spout in own)
            {
                spouts.Add(new QueuedEmitter
                {
                    Emitter = spout.Emitter,
                    PartIndex = spout.PartIndex,
                    Offset = spout.Offset * local,
                });
            }
        }

        HarvestedMesh? shell = null;
        if (StructureOf(chamber) is { } structure)
        {
            ulong shellIdent = ident | ChamberGeoBit;
            shell = HarvestChamberStruct(shellIdent, structure, chamber.SkinIds, Matrix4x4.Identity, token);
            if (shell is not null)
            {
                pieces.Add((shellIdent, Matrix4x4.Identity));
                reach.Merge(shell.BoundingBox.Min, shell.BoundingBox.Max);
            }
        }

        return new HarvestedMesh
        {
            ObjectId = ident,
            IsSetup = true,
            SetupParts = pieces,
            ParticleEmitters = spouts,
            EnvCellGeometry = shell,
            BoundingBox = reach.Box,
            SelectionSphere = new Sphere
            {
                Center = reach.Any ? (reach.Min + reach.Max) / 2f : Vector3.Zero,
                Radius = reach.Any ? Vector3.Distance(reach.Max, reach.Min) / 2.0f : 0f,
            },
        };
    }

    private static Matrix4x4 InvOf(Matrix4x4 m) => Matrix4x4.Invert(m, out Matrix4x4 inv) ? inv : Matrix4x4.Identity;

    private void CollectPieces(uint ident, Matrix4x4 carried, List<(ulong GfxObjId, Matrix4x4 Transform)> pieces, ref Extent reach, CancellationToken token, int zDepth)
    {
        if (zDepth > UpperPieceZDepth)
        {
            _logger.LogWarning("Max recursion depth reached while collecting parts for 0x{Id:X8}. Possible circular dependency", ident);
            return;
        }
        token.ThrowIfCancellationRequested();

        if (!_datFiles.TryLocatePreferred(ident, out IDatDatabase? database, out RecordKind kind))
            return;

        switch (kind)
        {
            case RecordKind.Setup when database.TryGet<RigSpec>(ident, out var rig):
                {
                    if (RestingStance(rig) is not { } stance)
                        return;
                    bool scaled = (rig.Bits & RigBits.HasDefaultScale) == RigBits.HasDefaultScale;
                    for (int idx = 0; idx < rig.PartIds.Count; ++idx)
                    {
                        Matrix4x4 own = Matrix4x4.Identity;
                        if (scaled && rig.DefaultScale.Count > idx)
                            own *= Matrix4x4.CreateScale(rig.DefaultScale[idx]);
                        if (stance.Poses is not null && idx < stance.Poses.Count)
                            own *= Posture(stance.Poses[idx]);
                        CollectPieces(rig.PartIds[idx], own * carried, pieces, ref reach, token, zDepth + 1);
                    }
                    break;
                }

            case RecordKind.EnvCell when database.TryGet<RoomCell>(ident, out var chamber):
                {
                    Matrix4x4 intoChamber = InvOf(Posture(chamber.Position));
                    if (StructureOf(chamber) is { } structure)
                    {
                        foreach (MeshVertex vert in structure.Vertices.ByIndex.Values)
                            reach.Shield(Vector3.Transform(vert.Position, carried));
                        reach.Any = true;
                        pieces.Add(((ulong)ident | ChamberGeoBit, carried));
                    }
                    foreach (PlacedObject stab in chamber.StaticObjects)
                        CollectPieces(stab.Id, Posture(stab.Pose) * intoChamber * carried, pieces, ref reach, token, zDepth + 1);
                    break;
                }

            case RecordKind.GfxObj:
                {
                    pieces.Add((ident, carried));
                    if (!database.TryGet<PartMesh>(ident, out var gfx))
                        return;
                    (Vector3 lo, Vector3 hi) = ReachOf(gfx, Vector3.One);
                    for (int corner = 0; corner < 8; ++corner)
                    {
                        Vector3 own = new Vector3(
                            (corner & 4) is 0 ? lo.X : hi.X,
                            (corner & 2) is 0 ? lo.Y : hi.Y,
                            (corner & 1) is 0 ? lo.Z : hi.Z);
                        reach.Shield(Vector3.Transform(own, carried));
                    }
                    reach.Any = true;
                    break;
                }
        }
    }

    // Resting placement first, then Default, then whatever the setup has
    private static MotionFrame? RestingStance(RigSpec rig)
    {
        if (rig.Placements.TryGetValue(PlacementId.Resting, out MotionFrame? cycle))
            return cycle;
        if (rig.Placements.TryGetValue(PlacementId.Default, out cycle))
            return cycle;
        return rig.Placements.Values.FirstOrDefault();
    }

    private void CollectSpouts(uint programIdent, List<QueuedEmitter> spouts, CancellationToken token)
    {
        if (_programs.PullKineticsProgram(programIdent) is not { } program)
            return;

        foreach (EffectStep listing in program.Steps)
        {
            if (listing.Cue is not CreateParticleCue tap)
                continue;
            if (!_datFiles.Portal.TryGet<EmitterDesc>(tap.EmitterSpecId, out var spout))
                continue;

            spouts.Add(new QueuedEmitter
            {
                Emitter = spout,
                PartIndex = tap.PartIndex,
                Offset = Posture(tap.Offset),
            });

            uint hw = spout.HwPartMeshId;
            uint sw = spout.PartMeshId;
            if (hw is not 0)
                FlankHarvest(hw, token);
            if (sw is not 0 && sw != hw)
                FlankHarvest(sw, token);
        }
    }

    private void FlankHarvest(uint gfxObjRefIdent, CancellationToken token)
    {
        if (Harvest(gfxObjRefIdent, false, token) is { } triMesh)
            _flankTap?.Invoke(triMesh);
    }
}
