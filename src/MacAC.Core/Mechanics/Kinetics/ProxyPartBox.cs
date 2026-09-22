using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>A part's contact sphere together with the box it fits in, both in the part's own frame.</summary>
public readonly record struct ProxyPartGeometry
{
    private ProxyPartGeometry(PackedContactSphere orb, Vector3 bboxLower, Vector3 bboxUpper)
    {
        Sphere = orb;
        BboxLower = bboxLower;
        BboxUpper = bboxUpper;
    }

    public PackedContactSphere Sphere { get; }

    public Vector3 BboxLower { get; }

    public Vector3 BboxUpper { get; }

    /// <summary>Uses the authored visual extent when there is one, else the sphere's own bounding cube.</summary>
    public static ProxyPartGeometry Create(PackedContactSphere orb, PackedGfxObjVisualExtent? visualLimits)
    {
        if (visualLimits is { } limits)
            return new ProxyPartGeometry(orb, limits.Min, limits.Max);

        Vector3 half = new Vector3(orb.Radius);
        return new ProxyPartGeometry(orb, orb.Origin - half, orb.Origin + half);
    }
}

/// <summary>An oriented part box placed in the world, refittable to any axis-aligned frame.</summary>
public readonly record struct ProxyPartBox
{
    private ProxyPartBox(Vector3 ownLower, Vector3 ownUpper, Vector3 realmLocus, Quaternion realmSpin)
    {
        OwnLower = ownLower;
        OwnUpper = ownUpper;
        WorldLocus = realmLocus;
        RealmSpin = realmSpin;
    }

    public Vector3 OwnLower { get; }

    public Vector3 OwnUpper { get; }

    public Vector3 WorldLocus { get; }

    public Quaternion RealmSpin { get; }

    public static ProxyPartBox FromForm(in ProxyShape form, Vector3 actorRealmLocus, Quaternion actorRealmSpin)
    {
        return new(
            form.OwnBoundsMin,
            form.OwnBoundsMax,
            actorRealmLocus + Vector3.Transform(form.OwnPlace, actorRealmSpin),
            actorRealmSpin * form.OwnSpin);
    }

    /// <summary>Axis-aligned bounds of the box relative to <paramref name="cycleOrigin"/>.</summary>
    public void RefitTo(Vector3 cycleOrigin, out Vector3 lower, out Vector3 upper)
    {
        Vector3 shift = WorldLocus - cycleOrigin;
        lower = new Vector3(float.MaxValue);
        upper = new Vector3(float.MinValue);
        for (int idx = 0; idx < 8; ++idx)
        {
            Vector3 realm = Vector3.Transform(Corner(idx), RealmSpin) + shift;
            lower = Vector3.Min(lower, realm);
            upper = Vector3.Max(upper, realm);
        }
    }

    /// <summary>Axis-aligned bounds of the box after mapping it through <paramref name="realmToOwn"/>.</summary>
    public void RefitToLocal(Matrix4x4 realmToOwn, out Vector3 lower, out Vector3 upper)
    {
        lower = new Vector3(float.MaxValue);
        upper = new Vector3(float.MinValue);
        for (int idx = 0; idx < 8; ++idx)
        {
            Vector3 realm = Vector3.Transform(Corner(idx), RealmSpin) + WorldLocus;
            Vector3 own = Vector3.Transform(realm, realmToOwn);
            lower = Vector3.Min(lower, own);
            upper = Vector3.Max(upper, own);
        }
    }

    // Corner idx (0..7) of the local box; bit 0/1/2 pick max on x/y/z
    private Vector3 Corner(int idx)
    {
        return new(
        (idx & 1) is 0 ? OwnLower.X : OwnUpper.X,
        (idx & 2) is 0 ? OwnLower.Y : OwnUpper.Y,
        (idx & 4) is 0 ? OwnLower.Z : OwnUpper.Z);
    }
}
