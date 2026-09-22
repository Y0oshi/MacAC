namespace MacAC.Mechanics.Realm;

public enum PortalAnimState
{
    Off = 0,
    WorldFadeOut = 1,
    TunnelFadeIn = 2,
    Tunnel = 3,
    TunnelContinue = 4,
    TunnelFadeOut = 5,
    WorldFadeIn = 6,
}

/// <summary>Why the teleport happened; decides which state the sequence starts in.</summary>
public enum PortalEntryKind
{
    Portal,
    Login,
    Death,
    Logout,
}

public enum PortalAnimEvent
{
    /// <summary>Begin(): sound_ui_enter_portal.</summary>
    PlayEnterSound,

    /// <summary>First tunnel-family frame: the portal viewport replaces the world.</summary>
    EnterTunnel,

    /// <summary>Tunnel → TunnelContinue: the world is loaded, place the player.</summary>
    Place,

    /// <summary>TunnelFadeOut → WorldFadeIn: sound_ui_exit_portal.</summary>
    PlayExitSound,

    /// <summary>WorldFadeIn → Off: send GameAction 0xA1.</summary>
    FireLoginComplete,
}

/// <summary>One frame of the portal transition, as the renderer needs it.</summary>
public readonly record struct PortalAnimFrame(
    PortalAnimState State,
    float ViewPlaneBlend,
    bool ShowTunnel,
    bool ShowPleaseWait);

public sealed class WarpAnimScheduler
{
    public const float FadeMoment = 1.0f;
    public const float LowerContinue = 2.0f;
    public const float UpperContinue = 5.0f;
    public const float TunnelCyclesPerSecond = 40.0f;
    public const int TunnelFinishCycle = 120;
    public const float QuitPaneLo = FadeMoment + 0.1f;
    public const float QuitPaneHi = FadeMoment + 0.3f;
    internal const short PreviousShownOutgoingAnimTier = 1001;

    private const int AnimHops = 100;
    private const float TierScaling = 1024f;

    private static readonly short[] AnimTiers = CalculateAnimTiers();

    private PortalAnimState _phase = PortalAnimState.Off;
    private float _sincePhaseBegin;
    private float _sinceContinueBegin;
    private bool _joinSfxDue;
    private bool _joinTunnelDue;

    public bool IsActive => _phase != PortalAnimState.Off;

    public PortalAnimState State => _phase;

    /// <summary>Portal, login and death start inside the tunnel; logout fades the world out first.</summary>
    public void Begin(PortalEntryKind sort)
    {
        _phase = sort == PortalEntryKind.Logout ? PortalAnimState.WorldFadeOut : PortalAnimState.Tunnel;
        _sincePhaseBegin = 0f;
        _sinceContinueBegin = 0f;
        _joinSfxDue = true;
        _joinTunnelDue = _phase == PortalAnimState.Tunnel;
    }

    public void Reset()
    {
        _phase = PortalAnimState.Off;
        _sincePhaseBegin = 0f;
        _sinceContinueBegin = 0f;
        _joinSfxDue = false;
        _joinTunnelDue = false;
    }

    public (PortalAnimFrame snapshot, IReadOnlyList<PortalAnimEvent> events) Tick(
        float dt,
        bool realmPrimed,
        int tunnelAnimCycle = 72,
        bool gripInTunnel = false)
    {
        var signals = new List<PortalAnimEvent>();
        if (_joinSfxDue)
        {
            signals.Add(PortalAnimEvent.PlayEnterSound);
            _joinSfxDue = false;
        }
        if (_joinTunnelDue)
        {
            signals.Add(PortalAnimEvent.EnterTunnel);
            _joinTunnelDue = false;
        }

        _sincePhaseBegin += dt;

        switch (_phase)
        {
            case PortalAnimState.WorldFadeOut:
                if (OutgoingViewRectGone())
                    Enter(PortalAnimState.TunnelFadeIn, proclaimTunnel: true);
                break;

            case PortalAnimState.TunnelFadeIn:
                if (_sincePhaseBegin >= FadeMoment)
                    Enter(PortalAnimState.Tunnel);
                break;

            case PortalAnimState.Tunnel:
                // Wait here for the destination to load.
                if (realmPrimed && !gripInTunnel)
                {
                    signals.Add(PortalAnimEvent.Place);
                    Enter(PortalAnimState.TunnelContinue);
                    _sinceContinueBegin = 0f;
                }
                break;

            case PortalAnimState.TunnelContinue:
                _sinceContinueBegin += dt;
                uint cyclesLeft = unchecked((uint)TunnelFinishCycle - (uint)tunnelAnimCycle);
                float secsLeft = cyclesLeft / TunnelCyclesPerSecond;
                bool inQuitPane = _sinceContinueBegin >= LowerContinue && secsLeft > QuitPaneLo && secsLeft < QuitPaneHi;
                if (inQuitPane || _sinceContinueBegin >= UpperContinue)
                    Enter(PortalAnimState.TunnelFadeOut);
                break;

            case PortalAnimState.TunnelFadeOut:
                if (OutgoingViewRectGone())
                {
                    Enter(PortalAnimState.WorldFadeIn);
                    signals.Add(PortalAnimEvent.PlayExitSound);
                }
                break;

            case PortalAnimState.WorldFadeIn:
                if (_sincePhaseBegin >= FadeMoment)
                {
                    Enter(PortalAnimState.Off);
                    signals.Add(PortalAnimEvent.FireLoginComplete);
                }
                break;
        }

        return (Frame(), events: signals);
    }

    /// <summary>Samples the retail animation table at t in [0, 1].</summary>
    public static short FetchCanonAnimTier(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        int negativeOrdinal = (int)Math.Truncate(-99.0 * t);
        return AnimTiers[-negativeOrdinal];
    }

    private bool OutgoingViewRectGone()
    {
        return FetchCanonAnimTier(_sincePhaseBegin / FadeMoment) > PreviousShownOutgoingAnimTier;
    }

    private void Enter(PortalAnimState upcoming, bool proclaimTunnel = false)
    {
        _phase = upcoming;
        _sincePhaseBegin = 0f;
        if (proclaimTunnel)
            _joinTunnelDue = true;
    }

    private PortalAnimFrame Frame()
    {
        bool tunnel = _phase is PortalAnimState.TunnelFadeIn or PortalAnimState.Tunnel
            or PortalAnimState.TunnelContinue or PortalAnimState.TunnelFadeOut;
        return new PortalAnimFrame(_phase, ViewPlaneBlend(_phase, _sincePhaseBegin), tunnel, _phase == PortalAnimState.TunnelContinue);
    }

    private static float ViewPlaneBlend(PortalAnimState phase, float passed)
    {
        float tier = FetchCanonAnimTier(Math.Clamp(passed / FadeMoment, 0f, 1f)) / TierScaling;
        return phase switch
        {
            // Game view distance out to the transition distance.
            PortalAnimState.WorldFadeOut or PortalAnimState.TunnelFadeOut => tier,
            // Transition distance back to the game view distance
            PortalAnimState.TunnelFadeIn or PortalAnimState.WorldFadeIn => 1f - tier,
            _ => 0f,
        };
    }

    // The client's start-up table: a truncated sine ramp, prefix-summed and normalised to 1024
    private static short[] CalculateAnimTiers()
    {
        const double canonPi = 3.1415920000000002d;
        const double reciprocal99 = 0.010101010101010102d;

        short[] tiers = new short[AnimHops];
        int sum = 0;
        for (int idx = 0; idx < tiers.Length; ++idx)
        {
            tiers[idx] = (short)Math.Truncate(Math.Sin(idx * canonPi * reciprocal99) * TierScaling);
            sum += tiers[idx];
        }

        int running = 0;
        for (int idx = 0; idx < tiers.Length; ++idx)
        {
            running += tiers[idx];
            tiers[idx] = (short)((running << 10) / sum);
        }
        return tiers;
    }
}
