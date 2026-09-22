using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public sealed class ContactLedger
{
    private bool _linkPlaneValid;
    private Plane _linkPlane;
    private uint _linkPlaneChamberIdent;
    private bool _linkPlaneIsWater;

    private bool _previousRecognizedLinkPlaneValid;
    private Plane _previousRecognizedLinkPlane;
    private uint _previousRecognizedLinkPlaneChamberIdent;
    private bool _previousRecognizedLinkPlaneIsWater;

    public bool ContactPlaneValid
    {
        get => _linkPlaneValid;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && _linkPlaneValid != value)
                KineticTelemetry.TraceCpBoolEmit("ContactPlaneValid", _linkPlaneValid, value);
            _linkPlaneValid = value;
        }
    }

    public Plane ContactPlane
    {
        get => _linkPlane;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && !PlaneEquals(_linkPlane, value))
                KineticTelemetry.TraceCpPlaneEmit("ContactPlane", _linkPlane, value);
            _linkPlane = value;
        }
    }

    public uint ContactPlaneCellId
    {
        get => _linkPlaneChamberIdent;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && _linkPlaneChamberIdent != value)
                KineticTelemetry.TraceCpChamberIdentEmit("ContactPlaneCellId", _linkPlaneChamberIdent, value);
            _linkPlaneChamberIdent = value;
        }
    }

    public bool ContactPlaneIsWater
    {
        get => _linkPlaneIsWater;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && _linkPlaneIsWater != value)
                KineticTelemetry.TraceCpBoolEmit("ContactPlaneIsWater", _linkPlaneIsWater, value);
            _linkPlaneIsWater = value;
        }
    }

    public bool LastKnownContactPlaneValid
    {
        get => _previousRecognizedLinkPlaneValid;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && _previousRecognizedLinkPlaneValid != value)
                KineticTelemetry.TraceCpBoolEmit("LastKnownContactPlaneValid", _previousRecognizedLinkPlaneValid, value);
            _previousRecognizedLinkPlaneValid = value;
        }
    }

    public Plane LastKnownContactPlane
    {
        get => _previousRecognizedLinkPlane;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && !PlaneEquals(_previousRecognizedLinkPlane, value))
                KineticTelemetry.TraceCpPlaneEmit("LastKnownContactPlane", _previousRecognizedLinkPlane, value);
            _previousRecognizedLinkPlane = value;
        }
    }

    public uint LastKnownContactPlaneCellId
    {
        get => _previousRecognizedLinkPlaneChamberIdent;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && _previousRecognizedLinkPlaneChamberIdent != value)
                KineticTelemetry.TraceCpChamberIdentEmit("LastKnownContactPlaneCellId", _previousRecognizedLinkPlaneChamberIdent, value);
            _previousRecognizedLinkPlaneChamberIdent = value;
        }
    }

    public bool LastKnownContactPlaneIsWater
    {
        get => _previousRecognizedLinkPlaneIsWater;
        set
        {
            if (KineticTelemetry.ProbeContactPlaneEnabled && _previousRecognizedLinkPlaneIsWater != value)
                KineticTelemetry.TraceCpBoolEmit("LastKnownContactPlaneIsWater", _previousRecognizedLinkPlaneIsWater, value);
            _previousRecognizedLinkPlaneIsWater = value;
        }
    }

    public void AssignLinkPlane(
        Plane plane,
        uint chamberIdent,
        bool isWater = false)
    {
        if (ContactPlaneValid
            && ContactPlaneCellId == chamberIdent
            && ContactPlaneIsWater == isWater
            && PlaneEquals(ContactPlane, plane))

            return;

        ++ContactPlaneWriteCount;
        ContactPlaneValid = true;
        ContactPlane = plane;
        ContactPlaneCellId = chamberIdent;
        ContactPlaneIsWater = isWater;

    }

    public bool SlidingNormalValid;
    public Vector3 SlidingNormal;       // XY only (Z zeroed)

    public bool CollisionNormalValid;
    public Vector3 CollisionNormal;

    public bool CollidedWithEnvironment;
    public int FramesStationaryFall;

    public Vector3 AdjustOffset;
    public readonly List<uint> CollideObjectGuids = new();
    public uint? LastCollidedObjectGuid;

    internal int ContactPlaneWriteCount { get; private set; }

    public void PrimeLinkPlane(
        Plane plane,
        uint chamberIdent,
        bool isWater = false)
    {
        AssignLinkPlane(plane, chamberIdent, isWater);

        LastKnownContactPlaneValid = true;
        LastKnownContactPlane = plane;
        LastKnownContactPlaneCellId = chamberIdent;
        LastKnownContactPlaneIsWater = isWater;
    }

    public void AssignSlidingNorm(Vector3 norm)
    {
        SlidingNormalValid = true;
        SlidingNormal = new Vector3(norm.X, norm.Y, 0f);
        if (SlidingNormal.LengthSquared() > KineticConstants.EpsilonSq)
            SlidingNormal = Vector3.Normalize(SlidingNormal);
    }

    public void AssignImpactNorm(Vector3 norm)
    {
        CollisionNormalValid = true;
        CollisionNormal = norm;
    }

    public void RestartForReuse()
    {
        _linkPlaneValid = false;
        _linkPlane = default;
        _linkPlaneChamberIdent = 0;
        _linkPlaneIsWater = false;
        _previousRecognizedLinkPlaneValid = false;
        _previousRecognizedLinkPlane = default;
        _previousRecognizedLinkPlaneChamberIdent = 0;
        _previousRecognizedLinkPlaneIsWater = false;

        SlidingNormalValid = false;
        SlidingNormal = Vector3.Zero;
        CollisionNormalValid = false;
        CollisionNormal = Vector3.Zero;
        CollidedWithEnvironment = false;
        FramesStationaryFall = 0;
        AdjustOffset = Vector3.Zero;
        CollideObjectGuids.Clear();
        LastCollidedObjectGuid = null;
        ContactPlaneWriteCount = 0;
    }

    internal void DuplicateFrom(ContactLedger src)
    {
        ArgumentNullException.ThrowIfNull(src);
        _linkPlaneValid = src._linkPlaneValid;
        _linkPlane = src._linkPlane;
        _linkPlaneChamberIdent = src._linkPlaneChamberIdent;
        _linkPlaneIsWater = src._linkPlaneIsWater;
        _previousRecognizedLinkPlaneValid = src._previousRecognizedLinkPlaneValid;
        _previousRecognizedLinkPlane = src._previousRecognizedLinkPlane;
        _previousRecognizedLinkPlaneChamberIdent = src._previousRecognizedLinkPlaneChamberIdent;
        _previousRecognizedLinkPlaneIsWater = src._previousRecognizedLinkPlaneIsWater;
        SlidingNormalValid = src.SlidingNormalValid;
        SlidingNormal = src.SlidingNormal;
        CollisionNormalValid = src.CollisionNormalValid;
        CollisionNormal = src.CollisionNormal;
        CollidedWithEnvironment = src.CollidedWithEnvironment;
        FramesStationaryFall = src.FramesStationaryFall;
        AdjustOffset = src.AdjustOffset;
        CollideObjectGuids.Clear();
        CollideObjectGuids.AddRange(src.CollideObjectGuids);
        LastCollidedObjectGuid = src.LastCollidedObjectGuid;
        ContactPlaneWriteCount = src.ContactPlaneWriteCount;
    }

    private static bool PlaneEquals(Plane a, Plane b)
    {
        return a.Normal.X == b.Normal.X &&
        a.Normal.Y == b.Normal.Y &&
        a.Normal.Z == b.Normal.Z &&
        a.D == b.D;
    }
}
