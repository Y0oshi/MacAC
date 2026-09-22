using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public readonly record struct ProxyShape
{
    private ProxyShape(
        uint gfxObjRefIdent,
        Vector3 ownLocus,
        Quaternion ownSpin,
        float scaling,
        ProxyContactType impactKind,
        float radius,
        float cylHeight,
        Vector3 limitsMiddle,
        Vector3 ownLimitsLower,
        Vector3 ownLimitsUpper)
    {
        GfxObjId = gfxObjRefIdent;
        OwnPlace = ownLocus;
        OwnSpin = ownSpin;
        Scale = scaling;
        ImpactKind = impactKind;
        Radius = radius;
        CylHeight = cylHeight;
        LimitsMiddle = limitsMiddle;
        OwnBoundsMin = ownLimitsLower;
        OwnBoundsMax = ownLimitsUpper;
    }

    public uint GfxObjId { get; }

    public Vector3 OwnPlace { get; }

    public Quaternion OwnSpin { get; }

    public float Scale { get; }

    public ProxyContactType ImpactKind { get; }

    public float Radius { get; }

    public float CylHeight { get; }

    public Vector3 LimitsMiddle { get; }

    public Vector3 OwnBoundsMin { get; }

    public Vector3 OwnBoundsMax { get; }

    public static ProxyShape Bsp(uint gfxObjRefIdent, Vector3 ownLocus, Quaternion ownSpin, float scaling, ProxyPartGeometry ownGeo)
    {
        return new(
            gfxObjRefIdent,
            ownLocus,
            ownSpin,
            scaling,
            ProxyContactType.BSP,
            ownGeo.Sphere.Radius * scaling,
            0f,
            ownGeo.Sphere.Origin * scaling,
            ownGeo.BboxLower * scaling,
            ownGeo.BboxUpper * scaling);
    }

    /// <summary>An upright cylinder standing on the part origin.</summary>
    public static ProxyShape Cylinder(uint gfxObjRefIdent, Vector3 ownLocus, Quaternion ownSpin, float scaling, float radius, float cylHeight)
    {
        return new(
            gfxObjRefIdent,
            ownLocus,
            ownSpin,
            scaling,
            ProxyContactType.Cylinder,
            radius,
            cylHeight,
            Vector3.Zero,
            new Vector3(-radius, -radius, 0f),
            new Vector3(radius, radius, cylHeight));
    }

    /// <summary>A sphere centred on the part origin.</summary>
    public static ProxyShape Sphere(uint gfxObjRefIdent, Vector3 ownLocus, Quaternion ownSpin, float scaling, float radius)
    {
        return new(
            gfxObjRefIdent,
            ownLocus,
            ownSpin,
            scaling,
            ProxyContactType.Sphere,
            radius,
            0f,
            Vector3.Zero,
            new Vector3(-radius),
            new Vector3(radius));
    }
}
