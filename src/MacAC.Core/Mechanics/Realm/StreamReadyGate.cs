namespace MacAC.Mechanics.Realm;

public static class StreamReadyGate
{
    public static bool ShouldFlow(bool onlineMannerTurnedOn, bool pursueMannerEverEntered, bool onlineInRealm, bool onlineMiddleRecognized)
    {
        bool expectingSignin = onlineMannerTurnedOn && !pursueMannerEverEntered;
        return !expectingSignin || (onlineInRealm && onlineMiddleRecognized);
    }
}
