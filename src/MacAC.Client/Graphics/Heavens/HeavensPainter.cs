using System.Diagnostics;
using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Landscape;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Heavens;

public sealed partial class HeavensPainter : IDisposable
{
    private readonly IDatAccess _datFiles;
    private readonly BitmapStash _textures;

    private HeavensParams _params;

    private readonly Dictionary<uint, List<SubTriMeshGpu>> _gpuByGfxObjRef = [];

    private readonly long _animBegunAtStamp = Stopwatch.GetTimestamp();

    internal float? AnimStageSecsOverride { get; init; }

    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 1_000_000f;

    private const uint StarLayerGfxObjId = 0x010015EFu;

    internal Func<bool>? EnhancedNightSkyActive { get; set; }

    private const float NightHeavensSeed = 11f;

    public void PaintHeavens(
        IClientCamera cam,
        Vector3 camRealmSpot,
        float dayRatio,
        DayGroupRow? cluster,
        SkyKeyframe keyframe,
        bool environOverrideEngaged = false)
        => PaintPass(cam, camRealmSpot, dayRatio, cluster, keyframe,
            postTableauPass: false, environOverrideEngaged: environOverrideEngaged);

    public void PaintWeather(
        IClientCamera cam,
        Vector3 camRealmSpot,
        float dayRatio,
        DayGroupRow? cluster,
        SkyKeyframe keyframe,
        bool environOverrideEngaged = false)
        => PaintPass(cam, camRealmSpot, dayRatio, cluster, keyframe,
            postTableauPass: true, environOverrideEngaged: environOverrideEngaged);

    private void PaintPass(
        IClientCamera cam,
        Vector3 camRealmSpot,
        float dayRatio,
        DayGroupRow? cluster,
        SkyKeyframe keyframe,
        bool postTableauPass,
        bool environOverrideEngaged)
    {
        if (cluster is null || cluster.SkyObjects.Count == 0) return;

        var heavensProj = HeavensProj.WithZDepthSpan(cam.Projection, Near, Far);

        var heavensLens = cam.View;
        heavensLens.M41 = 0f;
        heavensLens.M42 = 0f;
        heavensLens.M43 = 0f;

        _params.SkyView = heavensLens;
        _params.SkyProjection = heavensProj;

        _params.AmbientColor = keyframe.AmbientColor;
        _params.SunColor = keyframe.SunColor;
        _params.SunDir =
            MacAC.Mechanics.Realm.SkyStateSource.SunDirFromKeyframe(keyframe);

        var replaces = ChooseReplaces(cluster, dayRatio);

        float secsSinceBegin = AnimStageSecsOverride
            ?? PassedAnimSecs(
                _animBegunAtStamp,
                Stopwatch.GetTimestamp());

        for (int idx = 0; idx < cluster.SkyObjects.Count; idx++)
        {
            var objRef = cluster.SkyObjects[idx];
            if (objRef.IsPostTableau != postTableauPass) continue;
            if (!objRef.IsVisible(dayRatio)) continue;
            if (environOverrideEngaged && (objRef.Properties & 0x02u) != 0u)
                continue;

            // Apply per-keyframe replace overrides
            uint gfxObjRefIdent = objRef.GfxObjId;
            float bearingDeg = 0f;
            float seeThru = 0f;
            float replaceLuminosity = float.NaN;
            float replaceDiffuse = float.NaN;
            if (replaces.TryGetValue((uint)idx, out var rep))
            {
                if (rep.GfxObjId != 0) gfxObjRefIdent = rep.GfxObjId;
                if (rep.Rotate != 0f) bearingDeg = rep.Rotate;
                seeThru = Math.Clamp(rep.Transparent, 0f, 1f);
                if (rep.Luminosity > 0f) replaceLuminosity = rep.Luminosity;
                if (rep.UpperBright > 0f)
                    replaceDiffuse = rep.UpperBright;
            }
            if (gfxObjRefIdent == 0) continue;

            float spinDeg = objRef.LatestAngle(dayRatio);
            float bearingRad = bearingDeg * (MathF.PI / 180f);
            float spinRad = spinDeg * (MathF.PI / 180f);

            var model = Matrix4x4.CreateScale(1.0f)
                      * Matrix4x4.CreateRotationZ(-bearingRad)
                      * Matrix4x4.CreateRotationY(-spinRad);

            if (postTableauPass && objRef.IsWeather && (objRef.Properties & 0x08u) == 0u)
                model *= Matrix4x4.CreateTranslation(0f, 0f, -120f);

            _params.Model = model;

            float uShift = (objRef.BmpVelX * secsSinceBegin) % 1f;
            float vShift = (objRef.BmpVelY * secsSinceBegin) % 1f;
            _params.UvScroll = new Vector2(uShift, vShift);
            _params.Transparency = seeThru;

            SecureTriMeshUploaded(gfxObjRefIdent);
            if (!_gpuByGfxObjRef.TryGetValue(gfxObjRefIdent, out var subTriMeshes)) continue;

            foreach (var sub in subTriMeshes)
            {
                float effEmissive = float.IsNaN(replaceLuminosity)
                    ? sub.SurfLuminosity
                    : replaceLuminosity;
                float effDiffuse = float.IsNaN(replaceDiffuse)
                    ? sub.SurfDiffuse
                    : replaceDiffuse;
                _params.Emissive = effEmissive;
                _params.DiffuseFactor = effDiffuse;

                _params.SurfOpacity = sub.SurfOpacity;

                _params.ApplyFog = environOverrideEngaged && !sub.DisableFog ? 1f : 0f;

                bool needsRepeat = sub.NeedsUvRepeat
                    || objRef.BmpVelX != 0f
                    || objRef.BmpVelY != 0f;
                uint socket = TextureChartSocket(sub.SurfaceId, needsRepeat);
                bool nightHeavens = gfxObjRefIdent == StarLayerGfxObjId
                    && (EnhancedNightSkyActive?.Invoke() ?? false);
                PaintSubTriMeshRhi(sub, socket, nightHeavens);
            }
        }
    }

    internal static float PassedAnimSecs(long beginStamp, long latestStamp)
        => (float)Stopwatch.GetElapsedTime(beginStamp, latestStamp).TotalSeconds;

    private uint TextureChartSocket(uint canvasIdent, bool repeat) =>
        RhiTextureChartSocket(canvasIdent, repeat);

    private static Dictionary<uint, SkyObjectSwapRow> ChooseReplaces(
        DayGroupRow cluster, float dayRatio)
    {
        var outcome = new Dictionary<uint, SkyObjectSwapRow>();
        var times = cluster.HeavensTimes;
        if (times.Count == 0) return outcome;

        DatSkyKeyframeRow k1 = times[^1];
        for (int idx = 0; idx < times.Count; idx++)
        {
            if (times[idx].Keyframe.Begin <= dayRatio)
                k1 = times[idx];
            else
                break;
        }

        foreach (var r in k1.Replaces)
            outcome[r.ObjectIndex] = r;

        return outcome;
    }

    private void SecureTriMeshUploaded(uint gfxObjRefIdent)
    {
        if (_gpuByGfxObjRef.ContainsKey(gfxObjRefIdent)) return;

        if ((gfxObjRefIdent & 0xFF000000u) == 0x02000000u)
        {
            SecureRigUploaded(gfxObjRefIdent);
            return;
        }

        PartMesh? gfx = null;
        try { gfx = _datFiles.Get<PartMesh>(gfxObjRefIdent); }
        catch { gfx = null; }

        if (gfx is null)
        {
            _gpuByGfxObjRef[gfxObjRefIdent] = [];
            return;
        }

        System.Collections.Generic.IReadOnlyList<GfxObjPatch>? subTriMeshes = null;
        try { subTriMeshes = GfxObjRefTriMesh.Build(gfx, _datFiles); }
        catch { subTriMeshes = null; }

        if (subTriMeshes is null)
        {
            _gpuByGfxObjRef[gfxObjRefIdent] = [];
            return;
        }

        if (System.Environment.GetEnvironmentVariable("MACAC_DUMP_SKY") == "1")
            PrintGfxObjRefCanvases(gfxObjRefIdent, gfx, subTriMeshes);

        var gpuRoster = new List<SubTriMeshGpu>(subTriMeshes.Count);
        foreach (var patch in subTriMeshes)
            gpuRoster.Add(PushSubTriMesh(patch));
        _gpuByGfxObjRef[gfxObjRefIdent] = gpuRoster;
    }

    private void SecureRigUploaded(uint rigIdent)
    {
        RigSpec? rig = null;
        try { rig = _datFiles.Get<RigSpec>(rigIdent); }
        catch { rig = null; }

        if (rig is null)
        {
            _gpuByGfxObjRef[rigIdent] = [];
            return;
        }

        var pieces = RigTriMesh.Flatten(rig);
        var allSubs = new List<SubTriMeshGpu>(pieces.Count);
        foreach (var pieceRef in pieces)
        {
            PartMesh? pieceGfx = null;
            try { pieceGfx = _datFiles.Get<PartMesh>(pieceRef.GfxObjId); }
            catch { pieceGfx = null; }
            if (pieceGfx is null) continue;

            System.Collections.Generic.IReadOnlyList<GfxObjPatch>? pieceSubs = null;
            try { pieceSubs = GfxObjRefTriMesh.Build(pieceGfx, _datFiles); }
            catch { pieceSubs = null; }
            if (pieceSubs is null) continue;

            // Bake the part's local transform into the vertices.
            var pieceTx = pieceRef.PartTransform;
            foreach (var sub in pieceSubs)
            {
                var transformed = new MechVertex[sub.Vertices.Length];
                for (int idx = 0; idx < sub.Vertices.Length; idx++)
                {
                    var vertex = sub.Vertices[idx];
                    var p = Vector3.Transform(vertex.Position, pieceTx);
                    var num = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, pieceTx));
                    transformed[idx] = vertex with { Position = p, Normal = num };
                }
                var rebuilt = sub with { Vertices = transformed };
                allSubs.Add(PushSubTriMesh(rebuilt));
            }
        }
        _gpuByGfxObjRef[rigIdent] = allSubs;
    }

    private void PrintGfxObjRefCanvases(
        uint gfxObjRefIdent,
        PartMesh gfx,
        System.Collections.Generic.IReadOnlyList<GfxObjPatch> subTriMeshes)
    {
        Console.WriteLine(
            $"[sky-dump] GfxObj 0x{gfxObjRefIdent:X8} Surfaces.Count={gfx.SkinIds.Count} Polygons.Count={gfx.Facets.Count} SubMeshes.Count={subTriMeshes.Count}");

        for (int idx = 0; idx < gfx.SkinIds.Count; idx++)
        {
            uint canvasIdent = (uint)gfx.SkinIds[idx];
            Skin? canvas = null;
            try { canvas = _datFiles.Get<Skin>(canvasIdent); }
            catch { canvas = null; }

            if (canvas is null)
            {
                Console.WriteLine($"[sky-dump]   Surface[{idx}] 0x{canvasIdent:X8} -- (dat read failed)");
                continue;
            }

            uint rawKind = (uint)canvas.Bits;
            string labels = canvas.Bits.ToString();
            uint origBmp = canvas.TextureId;
            var trans = SeeThroughKindExtensions.FromCanvasKind(canvas.Bits);
            Console.WriteLine(
                $"[sky-dump]   Surface[{idx}] 0x{canvasIdent:X8} Type=0x{rawKind:X8} ({labels}) " +
                $"OrigTexture=0x{origBmp:X8} Translucency={trans} " +
                $"SurfLuminosity={canvas.Luminosity:F4} SurfaceTranslucency={canvas.Translucency:F4}");
        }
    }

    private SubTriMeshGpu PushSubTriMesh(GfxObjPatch patch) => PushSubTriMeshRhi(patch);

    public void Dispose() => TeardownRhi();

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    internal struct HeavensParams
    {
        public Matrix4x4 Model;            //   0
        public Matrix4x4 SkyView;          //  64
        public Matrix4x4 SkyProjection;    // 128
        public Vector3 AmbientColor;       // 192
        public float Emissive;             // 204
        public Vector3 SunColor;           // 208
        public float DiffuseFactor;        // 220
        public Vector3 SunDir;             // 224
        public float Transparency;         // 236
        public Vector2 UvScroll;           // 240
        public float ApplyFog;             // 248
        public float SurfOpacity;          // 252

        // 256 - the std140 size of the block, a whole number of vec4s
        public const int SizeInBytes = 256;
    }

    private sealed class SubTriMeshGpu
    {
        public MacAC.Client.Graphics.Gpu.IClientGpuBuffer? VertBuf;
        public MacAC.Client.Graphics.Gpu.IClientGpuBuffer? OrdinalBuf;
        public int IndexTally;
        public uint SurfaceId;
        public bool IsAdditive;
        public float SurfLuminosity;
        public float SurfDiffuse;
        public bool NeedsUvRepeat;
        public float SurfOpacity;
        public bool DisableFog;
    }
}
