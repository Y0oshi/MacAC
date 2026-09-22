namespace MacAC.Client.Controls;

internal interface IAvatarIdentitySource
{
    uint SrvOid { get; }
}

// Session-scoped identity slot shared by focused update owners
internal sealed class AvatarIdentityLedger(SimAvatarIdentityLedger runtime) : IAvatarIdentitySource
{
    private readonly SimAvatarIdentityLedger _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public AvatarIdentityLedger()
        : this(new SimAvatarIdentityLedger())
    {
    }

    public uint SrvOid
    {
        get => _runtime.ServerGuid;
        set => _runtime.ServerGuid = value;
    }
}

internal interface IAvatarKineticsHostSource
{
    ActorKineticsHarbor? Host { get; }
}

// The one mutable local physics-host slot
internal sealed class AvatarKineticsHostSlot : IAvatarKineticsHostSource
{
    public ActorKineticsHarbor? Host { get; set; }
}

internal interface IAvatarModeSource
{
    bool IsPlayerMode { get; }
    bool PursueMannerEverEntered { get; }
}

internal sealed class AvatarModeLedger : IAvatarModeSource
{
    public bool IsPlayerMode { get; set; }
    public bool PursueMannerEverEntered { get; set; }

    public void RestartSession()
    {
        IsPlayerMode = false;
        PursueMannerEverEntered = false;
    }
}

internal interface IViewRectAspectOrigin
{
    float Aspect { get; }
}

// Framebuffer aspect published by the window host
internal sealed class ViewportAspectLedger : IViewRectAspectOrigin
{
    public float Aspect { get; private set; } = 16f / 9f;

    public void Update(int width, int height)
    {
        if (width > 0 && height > 0)
            Aspect = width / (float)height;
    }
}
