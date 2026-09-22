using System.Globalization;
using System.Numerics;

namespace MacAC.Client.Realm;

internal sealed class OnlineRealmOriginLedger
{
    public int CenterX { get; private set; }
    public int CenterY { get; private set; }
    public bool IsKnown { get; private set; }

    public void AssignPlaceholder(int middleX, int middleY)
    {
        CenterX = middleX;
        CenterY = middleY;
        IsKnown = false;
    }

    public bool TryInitialize(int middleX, int middleY)
    {
        if (IsKnown)
            return false;

        CenterX = middleX;
        CenterY = middleY;
        IsKnown = true;
        return true;
    }

    public void Recenter(int middleX, int middleY)
    {
        CenterX = middleX;
        CenterY = middleY;
    }

    public (int X, int Y) FetchMiddle() => (CenterX, CenterY);

    public void SecureAgreesWithCoreCycle(
        uint coreMiddleLbIdent,
        uint projectingLbIdent)
    {
        TrySecureAgreesWithCoreCycle(
            coreMiddleLbIdent,
            projectingLbIdent,
            passageInFlight: false);
    }

    public bool TrySecureAgreesWithCoreCycle(
        uint coreMiddleLbIdent,
        uint projectingLbIdent,
        bool passageInFlight)
    {
        if (!IsKnown || coreMiddleLbIdent is 0u)
            return true;

        int coreMiddleX = (int)((coreMiddleLbIdent >> 24) & 0xFFu);
        int coreMiddleY = (int)((coreMiddleLbIdent >> 16) & 0xFFu);
        if (coreMiddleX == CenterX && coreMiddleY == CenterY)
            return true;

        return passageInFlight
            ? false
            : throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"World-frame owners disagree: Runtime centre "
            + $"({coreMiddleX},{coreMiddleY}) vs streamed origin "
            + $"({CenterX},{CenterY}) while projecting landblock "
            + $"0x{projectingLbIdent:X8}. That offsets the entity by "
            + $"({(coreMiddleX - CenterX) * 192f:F0}m,"
            + $"{(coreMiddleY - CenterY) * 192f:F0}m) from its geometry"));
    }

    public Vector3 CellLocalForSeed(Vector3 realmLocus, uint chamberIdent)
    {
        int lbX = (int)((chamberIdent >> 24) & 0xFFu);
        int lbY = (int)((chamberIdent >> 16) & 0xFFu);
        Vector3 origin = new Vector3(
            (lbX - CenterX) * 192f,
            (lbY - CenterY) * 192f,
            0f);
        return realmLocus - origin;
    }

    public void Reset() => IsKnown = false;
}
