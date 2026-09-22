using System.Numerics;

namespace MacAC.Mechanics.Illumination;

public enum LampFlavor
{
    /// <summary>Sun or moon: a direction only, infinite reach.</summary>
    Directional = 0,

    /// <summary>Torch, fireplace, spell aura.</summary>
    Point = 1,

    Spot = 2,
}

public sealed class LightEmitter
{
    private Vector3 _rankingOrigin;

    public LampFlavor Kind;

    public Vector3 RealmPosition;

    /// <summary>For spot and directional lights.</summary>
    public Vector3 RealmAhead;

    public Vector3 TintLinear = Vector3.One;

    public float Intensity = 1f;

    public float Range = 10f;

    /// <summary>Radians; spot lights only.</summary>
    public float ConeAngle;

    /// <summary>Attached entity id, or 0 for a world-global light.</summary>
    public uint HolderTag;

    public uint CellId;

    /// <summary>The SetLightHook latch.</summary>
    public bool IsLit = true;

    /// <summary>False means a DAT-baked static light (1/d³ falloff, range x1.3).</summary>
    public bool IsDynamic;

    public bool TracksHolderPosture;

    public Matrix4x4 OwnPosture = Matrix4x4.Identity;

    /// <summary>Scratch: squared distance to the viewer from the last ranking pass.</summary>
    public float DistanceSq;

    /// <summary>The point distance ranking measures from; the owner's root, not the light itself.</summary>
    public Vector3 RankingOrigin
    {
        get => _rankingOrigin;
        set
        {
            _rankingOrigin = value;
            HasRankingOrigin = true;
        }
    }

    /// <summary>True only once a ranking origin has been supplied.</summary>
    public bool HasRankingOrigin { get; private set; }
}

public readonly record struct CellAmbientLight(
    Vector3 AmbientColor,
    Vector3 SunColor,
    Vector3 SunDirection);
