namespace MacAC.Client.Paging;

internal static class ActorVanishProbe
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("MACAC_PROBE_ENT") == "1";

    public static uint AvatarGuid;

    public static void Record(string message)
    {
        if (Enabled) Console.WriteLine(message);
    }

    private static string _previousAvatarDyn = "";

    public static void TraceAvatarDynOnEdit(string condition)
    {
        if (!Enabled) return;
        if (condition == _previousAvatarDyn) return;
        _previousAvatarDyn = condition;
        Console.WriteLine("[dyn] player " + condition);
    }
}
