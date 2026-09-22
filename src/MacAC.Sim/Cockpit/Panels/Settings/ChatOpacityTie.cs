namespace MacAC.Cockpit.Panels.Settings;

public static class ChatOpacityTie
{
    public static (float DefaultOpacity, float ActiveOpacity) AssignDefault(float latestEngaged, float newDefault)
    {
        newDefault = Math.Clamp(newDefault, 0f, 1f);
        return (newDefault, Math.Max(latestEngaged, newDefault));
    }

    public static (float DefaultOpacity, float ActiveOpacity) ApplyEngaged(float latestDefault, float newEngaged)
    {
        newEngaged = Math.Clamp(newEngaged, 0f, 1f);
        return (Math.Min(latestDefault, newEngaged), newEngaged);
    }
}
