using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public enum ShiftVerdict
{
    Invalid = 0,
    OK = 1,
    Collided = 2,
    Adjusted = 3,
    Slid = 4,
}

internal enum ShiftCellContactPhase
{
    Environment,
    Building,
    Objects,
}

public enum SlotKind
{
    Transition = 0,
    Placement = 1,
    InitialPlacement = 2,
}

[Flags]
public enum MoverState : uint
{
    None = 0x000,
    Contact = 0x001,
    OnWalkable = 0x002,
    IsViewer = 0x004,
    PathClipped = 0x008,
    FreeRotate = 0x010,
    PerfectClip = 0x040,
    IsImpenetrable = 0x080,
    IsPlayer = 0x100,
    EdgeSlide = 0x200,
    IgnoreCreatures = 0x400,
    IsPK = 0x800,
    IsPKLite = 0x1000,
    CanBypassMoveRestrictions = 0x2000,
}

public static class KineticConstants
{
    public const float EPSILON = 0.0002f;
    public const float EpsilonSq = EPSILON * EPSILON;
    public const float LandingZ = 0.0871557f;
    public const float FloorZ = 0.6642f;
    public const float DefaultHopHeight = 0.01f;
    public const float Gravity = -9.8f;
    public const float MaxVelocity = 50.0f;
    public const float DummyOrbRadius = 0.1f;
}
