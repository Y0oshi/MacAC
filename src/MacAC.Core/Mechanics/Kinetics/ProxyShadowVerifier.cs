using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public readonly record struct ContactProxyStats(
    long Queries,
    long Samples,
    long Matches,
    long Mismatches,
    long Faults);

internal sealed class ProxyShadowVerifier
{
    private const int SchemaVersion = 1;

    private enum Pass
    {
        None,
        Flat,
        Graph,
    }

    private readonly int _specimenEvery;
    private readonly string _artifactFolder;
    private readonly Changeover _shade = new();
    private long _asks;
    private long _specimens;
    private long _fits;
    private long _mismatches;
    private long _flaws;
    private Pass _pass;

    public ProxyShadowVerifier(int sampleEvery, string artifactFolder)
    {
        if (sampleEvery <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleEvery));
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactFolder);
        _specimenEvery = sampleEvery;
        _artifactFolder = Path.GetFullPath(artifactFolder);
    }

    internal bool IsPlanarPass => _pass == Pass.Flat;
    internal bool IsGraphPass => _pass == Pass.Graph;

    internal ContactProxyStats Stats => new(_asks, _specimens, _fits, _mismatches, _flaws);

    // Counts a query; true when this one should be shadowed (never while a pass is already running)
    internal bool TrySpecimen(out long specimen)
    {
        specimen = 0;
        if (_pass != Pass.None)
            return false;
        if (++_asks % _specimenEvery is not 0)
            return false;
        specimen = ++_specimens;
        return true;
    }

    internal Changeover ReadyShade(Changeover src)
    {
        _shade.DuplicateFrom(src);
        return _shade;
    }

    internal void CommencePlanarPass() => Begin(Pass.Flat);
    internal void FinishPlanarPass() => End(Pass.Flat, "flat");
    internal void CommenceGraphPass() => Begin(Pass.Graph);
    internal void FinishGraphPass() => End(Pass.Graph, "graph");

    internal void CaptureBoolean(long specimen, string sort, uint srcIdent, bool graph, bool planar, string feed)
    {
        if (graph == planar)
            ++_fits;
        else
            CaptureMismatch(specimen, sort, srcIdent, "Result", graph.ToString(), planar.ToString(), feed);
    }

    internal void CaptureOrb(long specimen, string sort, uint srcIdent, PackedContactSphere graph, PackedContactSphere planar)
    {
        if (BitExact.Same(graph.Origin, planar.Origin) && BitExact.Same(graph.Radius, planar.Radius))
            ++_fits;
        else
            CaptureMismatch(specimen, sort, srcIdent, "RootBoundingSphere", BitExact.Show(graph), BitExact.Show(planar), string.Empty);
    }

    internal void CaptureChangeover(
        long specimen,
        string sort,
        uint srcIdent,
        ShiftVerdict graphPhase,
        ShiftVerdict planarPhase,
        Changeover graph,
        Changeover planar,
        string feed)
    {
        if (graphPhase != planarPhase)
        {
            CaptureMismatch(specimen, sort, srcIdent, "ShiftVerdict", graphPhase.ToString(), planarPhase.ToString(), feed);
            return;
        }

        if (ShiftExactComparer.TrySeekDifference(graph, planar, out string trail, out string graphVal, out string planarVal))
        {
            CaptureMismatch(specimen, sort, srcIdent, trail, graphVal, planarVal, feed);
            return;
        }

        ++_fits;
    }

    internal void CaptureFlaw(long specimen, string sort, uint srcIdent, Exception flaw, string feed, string arbiter = "graph")
    {
        ++_flaws;
        Write(new ProxyMismatchArtifact(
            SchemaVersion,
            specimen,
            sort,
            $"0x{srcIdent:X8}",
            arbiter == "flat" ? "GraphFault" : "FlatFault",
            $"{arbiter}-authoritative",
            $"{flaw.GetType().FullName}: {flaw.Message}",
            feed));
    }

    internal static string ComposeFeed(Vector3 middle, float radius) =>
        $"center={BitExact.Show(middle)};radius={BitExact.Show(radius)}";

    internal static string ComposeFeed(Vector3 lower, Vector3 upper) =>
        $"min={BitExact.Show(lower)};max={BitExact.Show(upper)}";

    internal static string ComposeFeed(
        Vector3 middle,
        float radius,
        bool hasSphere1,
        Vector3 sphere1Middle,
        float sphere1Radius,
        Vector3 latestMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm,
        Vector3 realmOrigin)
    {
        return $"sphere0={BitExact.Show(middle)}/{BitExact.Show(radius)};" +
        $"sphere1={(hasSphere1 ? BitExact.Show(sphere1Middle) : "none")}/" +
        $"{BitExact.Show(sphere1Radius)};current={BitExact.Show(latestMiddle)};" +
        $"localZ={BitExact.Show(ownSpaceZ)};scale={BitExact.Show(scaling)};" +
        $"rotation={BitExact.Show(ownToRealm)};origin={BitExact.Show(realmOrigin)}";
    }

    private void Begin(Pass pass)
    {
        if (_pass != Pass.None)
            throw new InvalidOperationException("Collision shadow passes can't overlap");
        _pass = pass;
    }

    private void End(Pass pass, string caption)
    {
        if (_pass != pass)
            throw new InvalidOperationException($"No collision shadow {caption} pass is active");
        _pass = Pass.None;
    }

    private void CaptureMismatch(long specimen, string sort, uint srcIdent, string difference, string graph, string planar, string feed)
    {
        ++_mismatches;
        Write(new ProxyMismatchArtifact(SchemaVersion, specimen, sort, $"0x{srcIdent:X8}", difference, graph, planar, feed));
    }

    private void Write(ProxyMismatchArtifact artifact)
    {
        Directory.CreateDirectory(_artifactFolder);
        string trail = Path.Combine(_artifactFolder, FormattableString.Invariant($"collision-shadow-{artifact.Sample:D8}.json"));
        File.WriteAllText(trail, JsonSerializer.Serialize(artifact, ProxyShadowJsonContext.Default.ProxyMismatchArtifact));
    }
}

internal sealed record ProxyMismatchArtifact(
    int SchemaVersion,
    long Sample,
    string Kind,
    string SourceId,
    string Difference,
    string Graph,
    string Flat,
    string Input);

[JsonSerializable(typeof(ProxyMismatchArtifact))]
internal sealed partial class ProxyShadowJsonContext : JsonSerializerContext;

// Bit-for-bit float comparison and hex rendering shared by the referee
internal static class BitExact
{
    public static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    public static bool Same(Vector3 a, Vector3 b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    public static bool Same(Quaternion a, Quaternion b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z) && Same(a.W, b.W);

    public static bool Same(Plane a, Plane b) => Same(a.Normal, b.Normal) && Same(a.D, b.D);

    public static string Show(float val) => $"0x{BitConverter.SingleToInt32Bits(val):X8}";

    public static string Show(Vector3 v) => $"{Show(v.X)},{Show(v.Y)},{Show(v.Z)}";

    public static string Show(Quaternion q) => $"{Show(q.X)},{Show(q.Y)},{Show(q.Z)},{Show(q.W)}";

    public static string Show(Plane p) => $"{Show(p.Normal)}/{Show(p.D)}";

    public static string Show(PackedContactSphere sphere) => $"{Show(sphere.Origin)}/{Show(sphere.Radius)}";

    // Generic equality that treats floats and float-bearing structs bitwise
    public static bool Same<T>(T a, T b)
    {
        return (a, b) switch
        {
            (float x, float y) => Same(x, y),
            (Vector3 x, Vector3 y) => Same(x, y),
            (Quaternion x, Quaternion y) => Same(x, y),
            (Plane x, Plane y) => Same(x, y),
            _ => EqualityComparer<T>.Default.Equals(a, b),
        };
    }

    public static string Show<T>(T val)
    {
        return val switch
        {
            null => "null",
            float f => Show(f),
            Vector3 v => Show(v),
            Quaternion q => Show(q),
            Plane p => Show(p),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => val.ToString() ?? string.Empty,
        };
    }
}

// Finds the first field on which two transition records disagree, walking them in a fixed order
internal static class ShiftExactComparer
{
    // Accumulates comparisons and remembers the first difference
    private sealed class Probe
    {
        public string Path = string.Empty;
        public string Graph = string.Empty;
        public string Flat = string.Empty;

        public bool Hit => Path.Length is not 0;

        public Probe Field<T>(string trail, T graph, T planar)
        {
            if (!Hit && !BitExact.Same(graph, planar))
                Note(trail, BitExact.Show(graph), BitExact.Show(planar));
            return this;
        }

        public Probe Spheres(string trail, Orb[] graph, Orb[] planar)
        {
            for (int idx = 0; idx < graph.Length && !Hit; ++idx)
            {
                Field($"{trail}[{idx}].Origin", graph[idx].Center, planar[idx].Center);
                Field($"{trail}[{idx}].Radius", graph[idx].Radius, planar[idx].Radius);
            }
            return this;
        }

        public Probe Vectors(string trail, Vector3[]? graph, Vector3[]? planar)
        {
            if (Hit)
                return this;
            if (graph is null || planar is null)
            {
                if (graph is not null || planar is not null)
                    Note(trail, graph is null ? "null" : "array", planar is null ? "null" : "array");
                return this;
            }
            if (graph.Length != planar.Length)
            {
                Note(trail + ".Length", Count(graph.Length), Count(planar.Length));
                return this;
            }
            for (int idx = 0; idx < graph.Length && !Hit; ++idx)
                Field($"{trail}[{idx}]", graph[idx], planar[idx]);
            return this;
        }

        public Probe List<T>(string trail, IReadOnlyList<T> graph, IReadOnlyList<T> planar)
        {
            if (Hit)
                return this;
            if (graph.Count != planar.Count)
            {
                Note(trail + ".Count", Count(graph.Count), Count(planar.Count));
                return this;
            }
            for (int idx = 0; idx < graph.Count; ++idx)
            {
                if (!EqualityComparer<T>.Default.Equals(graph[idx], planar[idx]))
                {
                    Note($"{trail}[{idx}]", BitExact.Show(graph[idx]), BitExact.Show(planar[idx]));
                    return this;
                }
            }
            return this;
        }

        private void Note(string trail, string graph, string planar)
        {
            Path = trail;
            Graph = graph;
            Flat = planar;
        }

        private static string Count(int num) => num.ToString(CultureInfo.InvariantCulture);
    }

    internal static bool TrySeekDifference(Changeover graph, Changeover planar, out string trail, out string graphVal, out string planarVal)
    {
        Probe sensor = new Probe();
        Mover(sensor, graph.MoverFacts, planar.MoverFacts);
        if (!sensor.Hit)
            Sweep(sensor, graph.SweepPath, planar.SweepPath);
        if (!sensor.Hit)
            Register(sensor, graph.ContactLedger, planar.ContactLedger);

        trail = sensor.Path;
        graphVal = sensor.Graph;
        planarVal = sensor.Flat;
        return sensor.Hit;
    }

    private static void Mover(Probe probe, MoverFacts facts, MoverFacts b)
    {
        probe
        .Field("MoverFacts.State", facts.State, b.State)
        .Field("MoverFacts.StepUpHeight", facts.StepUpHeight, b.StepUpHeight)
        .Field("MoverFacts.StepDownHeight", facts.StepDownHeight, b.StepDownHeight)
        .Field("MoverFacts.Ethereal", facts.Ethereal, b.Ethereal)
        .Field("MoverFacts.StepDown", facts.StepDown, b.StepDown)
        .Field("MoverFacts.Scale", facts.Scale, b.Scale)
        .Field("MoverFacts.MoverPhysicsState", facts.MoverPhysicsState, b.MoverPhysicsState)
        .Field("MoverFacts.TargetId", facts.TargetId, b.TargetId)
        .Field("MoverFacts.SelfEntityId", facts.SelfEntityId, b.SelfEntityId)
        .Field("MoverFacts.MoverHasGravity", facts.MoverHasGravity, b.MoverHasGravity)
        .Field("MoverFacts.VelocityKilled", facts.VelocityKilled, b.VelocityKilled);
    }

    private static void Register(Probe probe, ContactLedger ledger, ContactLedger b)
    {
        probe
        .Field("ContactLedger.ContactPlaneValid", ledger.ContactPlaneValid, b.ContactPlaneValid)
        .Field("ContactLedger.ContactPlane", ledger.ContactPlane, b.ContactPlane)
        .Field("ContactLedger.ContactPlaneCellId", ledger.ContactPlaneCellId, b.ContactPlaneCellId)
        .Field("ContactLedger.ContactPlaneIsWater", ledger.ContactPlaneIsWater, b.ContactPlaneIsWater)
        .Field("ContactLedger.LastKnownContactPlaneValid", ledger.LastKnownContactPlaneValid, b.LastKnownContactPlaneValid)
        .Field("ContactLedger.LastKnownContactPlane", ledger.LastKnownContactPlane, b.LastKnownContactPlane)
        .Field("ContactLedger.LastKnownContactPlaneCellId", ledger.LastKnownContactPlaneCellId, b.LastKnownContactPlaneCellId)
        .Field("ContactLedger.LastKnownContactPlaneIsWater", ledger.LastKnownContactPlaneIsWater, b.LastKnownContactPlaneIsWater)
        .Field("ContactLedger.SlidingNormalValid", ledger.SlidingNormalValid, b.SlidingNormalValid)
        .Field("ContactLedger.SlidingNormal", ledger.SlidingNormal, b.SlidingNormal)
        .Field("ContactLedger.CollisionNormalValid", ledger.CollisionNormalValid, b.CollisionNormalValid)
        .Field("ContactLedger.CollisionNormal", ledger.CollisionNormal, b.CollisionNormal)
        .Field("ContactLedger.CollidedWithEnvironment", ledger.CollidedWithEnvironment, b.CollidedWithEnvironment)
        .Field("ContactLedger.FramesStationaryFall", ledger.FramesStationaryFall, b.FramesStationaryFall)
        .Field("ContactLedger.AdjustOffset", ledger.AdjustOffset, b.AdjustOffset)
        .Field("ContactLedger.LastCollidedObjectGuid", ledger.LastCollidedObjectGuid, b.LastCollidedObjectGuid)
        .Field("ContactLedger.ContactPlaneWriteCount", ledger.ContactPlaneWriteCount, b.ContactPlaneWriteCount)
        .List("ContactLedger.CollideObjectGuids", ledger.CollideObjectGuids, b.CollideObjectGuids);
    }

    private static void Sweep(Probe probe, SweepPath path, SweepPath b)
    {
        probe
        .Field("SweepPath.NumSphere", path.NumSphere, b.NumSphere)
        .Spheres("SweepPath.LocalSphere", path.LocalSphere, b.LocalSphere)
        .Spheres("SweepPath.GlobalSphere", path.GlobalSphere, b.GlobalSphere)
        .Spheres("SweepPath.GlobalCurrCenter", path.GlobalCurrCenter, b.GlobalCurrCenter)
        .Field("SweepPath.BeginPos", path.BeginPos, b.BeginPos)
        .Field("SweepPath.EndPos", path.EndPos, b.EndPos)
        .Field("SweepPath.CurPos", path.CurPos, b.CurPos)
        .Field("SweepPath.CheckPos", path.CheckPos, b.CheckPos)
        .Field("SweepPath.BeginOrientation", path.BeginOrientation, b.BeginOrientation)
        .Field("SweepPath.EndOrientation", path.EndOrientation, b.EndOrientation)
        .Field("SweepPath.CurOrientation", path.CurOrientation, b.CurOrientation)
        .Field("SweepPath.CheckOrientation", path.CheckOrientation, b.CheckOrientation)
        .Field("SweepPath.CurCellId", path.CurCellId, b.CurCellId)
        .Field("SweepPath.CheckCellId", path.CheckCellId, b.CheckCellId)
        .Field("SweepPath.CarriedBlockOrigin", path.CarriedBlockOrigin, b.CarriedBlockOrigin)
        .Field("SweepPath.GlobalOffset", path.GlobalOffset, b.GlobalOffset)
        .Field("SweepPath.StepUp", path.StepUp, b.StepUp)
        .Field("SweepPath.StepUpNormal", path.StepUpNormal, b.StepUpNormal)
        .Field("SweepPath.Collide", path.Collide, b.Collide)
        .Field("SweepPath.StepDown", path.StepDown, b.StepDown)
        .Field("SweepPath.StepDownAmt", path.StepDownAmt, b.StepDownAmt)
        .Field("SweepPath.WalkInterp", path.WalkInterp, b.WalkInterp)
        .Field("SweepPath.WalkableValid", path.WalkableValid, b.WalkableValid)
        .Field("SweepPath.WalkablePlane", path.WalkablePlane, b.WalkablePlane)
        .Vectors("SweepPath.WalkableVertices", path.WalkableVertices, b.WalkableVertices)
        .Field("SweepPath.WalkableUp", path.WalkableUp, b.WalkableUp)
        .Field("SweepPath.WalkableAllowance", path.WalkableAllowance, b.WalkableAllowance)
        .Field("SweepPath.LastWalkableValid", path.LastWalkableValid, b.LastWalkableValid)
        .Field("SweepPath.LastWalkablePlane", path.LastWalkablePlane, b.LastWalkablePlane)
        .Vectors("SweepPath.LastWalkableVertices", path.LastWalkableVertices, b.LastWalkableVertices)
        .Field("SweepPath.LastWalkableUp", path.LastWalkableUp, b.LastWalkableUp)
        .Field("SweepPath.BackupCheckPos", path.BackupCheckPos, b.BackupCheckPos)
        .Field("SweepPath.BackupCheckCellId", path.BackupCheckCellId, b.BackupCheckCellId)
        .Field("SweepPath.NegPolyHit", path.NegPolyHit, b.NegPolyHit)
        .Field("SweepPath.NegStepUp", path.NegStepUp, b.NegStepUp)
        .Field("SweepPath.NegCollisionNormal", path.NegCollisionNormal, b.NegCollisionNormal)
        .Field("SweepPath.CheckWalkable", path.CheckWalkable, b.CheckWalkable)
        .Field("SweepPath.InsertType", path.InsertType, b.InsertType)
        .Field("SweepPath.PlacementAllowsSliding", path.PlacementAllowsSliding, b.PlacementAllowsSliding)
        .Field("SweepPath.ObstructionEthereal", path.ObstructionEthereal, b.ObstructionEthereal)
        .Field("SweepPath.BldgCheck", path.BldgCheck, b.BldgCheck)
        .Field("SweepPath.HitsInteriorCell", path.HitsInteriorCell, b.HitsInteriorCell)
        .List("SweepPath.CellCandidates", path.CellCandidates.SequencedIdents, b.CellCandidates.SequencedIdents);
    }
}
