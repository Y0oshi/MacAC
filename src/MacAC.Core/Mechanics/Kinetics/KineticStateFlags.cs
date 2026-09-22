namespace MacAC.Mechanics.Kinetics;

/// <summary>Retail PhysicsState bits (+0xA8).</summary>
[Flags]
public enum KineticStateFlags : uint
{
    None = 0x00000000,
    Static = 0x00000001,
    ReservedUnused1 = 0x00000002,
    Ethereal = 0x00000004,
    ReportCollisions = 0x00000008,
    IgnoreCollisions = 0x00000010,
    NoDraw = 0x00000020,
    Missile = 0x00000040,
    Pushable = 0x00000080,
    AlignPath = 0x00000100,
    PathClipped = 0x00000200,
    Gravity = 0x00000400,
    Lighting = 0x00000800,
    ParticleEmitter = 0x00001000,
    ReservedUnused2 = 0x00002000,
    Hidden = 0x00004000,
    ScriptedCollision = 0x00008000,
    HasPhysicsBsp = 0x00010000,
    Inelastic = 0x00020000,
    HasDefaultAnim = 0x00040000,
    HasDefaultScript = 0x00080000,
    Cloaked = 0x00100000,
    ReportAsEnvironment = 0x00200000,
    EdgeSlide = 0x00400000,
    Sledding = 0x00800000,
    Frozen = 0x01000000,
}

/// <summary>Retail transient_state bits (+0xAC).</summary>
[Flags]
public enum TransientPhaseFlagSet : uint
{
    None = 0,
    /// <summary>bit 0 - touching any surface.</summary>
    Contact = 0x00000001,
    /// <summary>bit 1 - standing on a walkable surface.</summary>
    OnWalkable = 0x00000002,
    Sliding = 0x00000004,
    /// <summary>bit 3 - WATER_CONTACT_TS.</summary>
    WaterContact = 0x00000008,
    /// <summary>bit 4 - fsf == 1.</summary>
    StationaryFall = 0x00000010,
    /// <summary>bit 5 - fsf == 2.</summary>
    StationaryStop = 0x00000020,
    /// <summary>bit 6 - fsf == 3.</summary>
    StationaryStuck = 0x00000040,
    /// <summary>bit 7 - object needs per-frame update.</summary>
    Active = 0x00000080,
    CheckEthereal = 0x00000100,
}
