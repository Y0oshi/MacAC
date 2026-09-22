using System.Numerics;

namespace MacAC.Mechanics.Effects;

public enum MoteKind
{
    Unknown = 0,
    Still = 1,
    LocalVelocity = 2,
    ParabolicLVGA = 3,
    ParabolicLVGAGR = 4,
    Swarm = 5,
    Explode = 6,
    Implode = 7,
    ParabolicLVLA = 8,
    ParabolicLVLALR = 9,
    ParabolicGVGA = 10,
    ParabolicGVGAGR = 11,
    GlobalVelocity = 12,
    NumParticleType = 13,
}

public enum EmitterSort
{
    Unknown = 0,
    BirthratePerSec = 1,
    BirthratePerMeter = 2,
}

/// <summary>Which part of the frame an emitter draws in.</summary>
public enum ParticleDrawPass
{
    Scene = 0,
    SkyPreScene = 1,
    SkyPostScene = 2,
}

public enum LooseEmitterCellScope
{
    Any = 0,
    OutdoorCells = 1,
    InteriorCells = 2,
}

public enum ParticleVisibilityRules
{
    World = 0,
    Examination = 1,
    PassOwned = 2,
}

[Flags]
public enum EmitterBits : uint
{
    None = 0,
    Additive = 0x01,
    Billboard = 0x02,
    FaceCamera = 0x04,
    AttachLocal = 0x08,
}

/// <summary>An emitter definition, projected from the DAT ParticleEmitter record.</summary>
public sealed class EmitterSpec
{
    public float UpperDowngradeGap { get; init; } = 100f;

    public uint DatIdent { get; init; }

    public MoteKind Type { get; init; }

    public EmitterSort SpoutSort { get; init; } = EmitterSort.BirthratePerSec;

    public EmitterBits Flags { get; init; }

    public uint TextureCanvasIdent { get; init; }

    public uint GfxObjId { get; init; }

    public uint HwGfxObjId { get; init; }

    public uint SfxOnSummon { get; init; }

    // Emission
    public float Birthrate { get; init; }

    public float EmitRate { get; init; }

    public int MaxParticles { get; init; }

    public int InitialParticles { get; init; }

    public int TotalParticles { get; init; }

    public float LifespanLower { get; init; }

    public float LifespanUpper { get; init; }

    public float Lifespan { get; init; }

    public float LifespanRand { get; init; }

    public float BeginDelay { get; init; }

    public float SumInterval { get; init; }

    // Spawn shape.
    public Vector3 OffsetDir { get; init; } = new(0, 0, 1);

    public float MinOffset { get; init; }

    public float MaxOffset { get; init; }

    public float SummonDiskRadius { get; init; }

    // Motion.
    public Vector3 StartingVel { get; init; }

    public float VelJitter { get; init; }

    public Vector3 Gravity { get; init; } = new(0, 0, -9.8f);

    public Vector3 A { get; init; }

    public float MinA { get; init; } = 1f;

    public float MaxA { get; init; } = 1f;

    public Vector3 B { get; init; }

    public float MinB { get; init; } = 1f;

    public float MaxB { get; init; } = 1f;

    public Vector3 C { get; init; }

    public float MinC { get; init; } = 1f;

    public float MaxC { get; init; } = 1f;

    // Look over a particle's life.
    public uint BeginTintArgb { get; init; } = 0xFFFFFFFF;

    public uint FinishTintArgb { get; init; } = 0xFFFFFFFF;

    public float BeginAlpha { get; init; } = 1f;

    public float FinishAlpha { get; init; }

    public float BeginDims { get; init; } = 0.5f;

    public float FinishDims { get; init; } = 0.5f;

    public float ScaleRand { get; init; }

    public float TransRand { get; init; }

    public float BeginSpin { get; init; }

    public float FinishSpin { get; init; }
}

public enum KineticScriptHookType
{
    PlaySound = 1,
    AnimationDone = 2,
    CreateParticle = 18,
    DestroyParticle = 19,
}

public sealed record KineticScriptHook(
    float StartTime,
    KineticScriptHookType Type,
    uint RefDataId,
    int PartIndex,
    Vector3 Offset,
    bool IsParentLocal);

/// <summary>A timeline of effect hooks, played against one target object.</summary>
public sealed class KineticsProgram
{
    public uint ProgramIdent { get; init; }

    public IReadOnlyList<KineticScriptHook> Hooks { get; init; } = [];
}

/// <summary>One live particle, owned by its emitter's array.</summary>
public struct Mote
{
    public Vector3 EmissionOrigin;
    public Quaternion SummonSpin;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Offset;
    public Vector3 A;
    public Vector3 B;
    public Vector3 C;
    public float SpawnedAt;
    public float FrozenMomentAtSummon;
    public float Lifespan;
    public float Age;
    public float BeginSize;
    public float FinishSize;
    public float StartAlpha;
    public float EndAlpha;
    public uint ColorArgb;
    public float Size;
    public float Rotation;
    public bool Alive;
}

/// <summary>A running emitter: its spec, its anchor, and its particle pool.</summary>
public sealed class MoteSpout
{
    public int Handle { get; init; }

    public EmitterSpec Desc { get; init; } = null!;

    public Mote[] Particles { get; init; } = null!;

    public ParticleDrawPass RasterizePass { get; init; }

    public Vector3 MooringSpot { get; set; }

    public Vector3 HolderLocus { get; set; }

    public uint HolderChamberIdent { get; set; }

    public Quaternion MooringRot { get; set; } = Quaternion.Identity;

    public uint AffixedObjectIdent { get; set; }

    public int AffixedPieceOrdinal { get; set; } = -1;

    public ParticleVisibilityRules VisRule { get; set; }

    public bool ExhibitShown { get; set; } = true;

    public bool SimulationTurnedOn { get; set; } = true;

    public bool LensEligible { get; set; } = true;

    public bool DegradedOut { get; set; }

    public int ActiveCount;
    public float EmittedAccumulator;
    public float StartedAt;
    public float PreviousEmitMoment;

    public float FrozenMoment;

    public Vector3 PreviousEmitShift;
    public int SumEmitted;
    public bool Finished;
}

/// <summary>What the renderer and scripts need from the particle simulation.</summary>
public interface IParticleField
{
    int EngagedMoteTally { get; }

    int EngagedSpoutTally { get; }

    int SummonSpout(
        EmitterSpec descriptor,
        Vector3 mooring,
        Quaternion? rot = null,
        uint affixedObjectIdent = 0,
        int affixedPieceOrdinal = -1,
        ParticleDrawPass rasterizePass = ParticleDrawPass.Scene,
        ParticleVisibilityRules visRule = ParticleVisibilityRules.World);

    void PlayProgram(uint programIdent, uint markObjectIdent, float modifier = 1f);

    void Tick(float dt);

    void HaltSpout(int hnd, bool fadeOut);
}
