using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Play;

public interface ISimLocomotionInputSource
{
    LocomotionInput Capture();
}

public interface ISimAvatarFrameHarbor
{
    bool CanAdvancePlayer { get; }

    AvatarLocomotionDriver? Controller { get; }

    bool IsConcealed { get; }

    CanonClockVerdict ObjectTimerDisposition { get; }

    uint LocateOwnActorIdent();

    void ProcessTargeting();

    void Project(AvatarLocomotionDriver driver, LocomotionResult travel, bool concealed);

    void TransmitPreNetwork(AvatarLocomotionDriver driver, LocomotionResult travel, bool concealed);

    void TransmitPostNetwork(AvatarLocomotionDriver driver, bool concealed);
}

public readonly record struct SimAvatarPresentationFrame(LocomotionResult Movement, bool Hidden, bool AdvancedBeforeNetwork);

public sealed class SimAvatarFrameDriver(ISimAvatarFrameHarbor host, ISimLocomotionInputSource input, Action? broadcastTravel = null)
{
    private readonly ISimAvatarFrameHarbor _hub = host ?? throw new ArgumentNullException(nameof(host));
    private readonly ISimLocomotionInputSource _feed = input ?? throw new ArgumentNullException(nameof(input));
    private readonly Action? _broadcastTravel = broadcastTravel;
    private HalfFrame? _half;

    // The pre-network result, kept until the post-network phase consumes it
    private readonly record struct HalfFrame(
        AvatarLocomotionDriver Controller,
        LocomotionResult Movement,
        bool ObjectAdvanced,
        bool Hidden,
        bool ObjectQuantumAdvanced);

    public bool ConcealedPiecePostureStale => _half is { Hidden: true, ObjectQuantumAdvanced: true };

    public void AdvanceBeforeNetwork(float diffSecs)
    {
        _half = null;
        if (Live() is not { } driver)
            return;

        if (!float.IsFinite(diffSecs) || diffSecs <= 0f)
        {
            Grip(driver, _hub.IsConcealed);
            return;
        }

        driver.OwnEntityId = _hub.LocateOwnActorIdent();

        bool concealed = _hub.IsConcealed;
        if (_hub.ObjectTimerDisposition is CanonClockVerdict.Suspend)
        {
            driver.SuspendObjectRefresh(diffSecs);
            Grip(driver, concealed);
            return;
        }

        LocomotionResult travel = concealed
            ? driver.BeatConcealed(diffSecs, _hub.ProcessTargeting)
            : driver.Update(diffSecs, _feed.Capture(), _hub.ProcessTargeting);

        _hub.Project(driver, travel, concealed);
        _hub.TransmitPreNetwork(driver, travel, concealed);
        _half = new HalfFrame(driver, travel, ObjectAdvanced: true, concealed, driver.AdvancedObjectQuantumPreviousBeat);
    }

    public void PerformPostNetworkDirectiveStage()
    {
        if (Live() is not { } driver)
            return;

        bool concealed = _hub.IsConcealed;
        if (_half is { } half && ReferenceEquals(half.Controller, driver))
        {
            var travel = Refreshed(half.Movement, driver);
            _hub.Project(driver, travel, concealed);
            _half = half with { Movement = travel };
            if (!half.ObjectAdvanced)
                return;
        }

        _hub.TransmitPostNetwork(driver, concealed);
        _broadcastTravel?.Invoke();
    }

    public bool TryGetPresentationAfterNetwork(out SimAvatarPresentationFrame cycle)
    {
        cycle = default;
        if (Live() is not { } driver)
            return false;

        bool concealed = _hub.IsConcealed;
        if (_half is { } half && ReferenceEquals(half.Controller, driver))
        {
            cycle = new SimAvatarPresentationFrame(Refreshed(half.Movement, driver), concealed, AdvancedBeforeNetwork: half.ObjectAdvanced);
            return true;
        }

        var starting = driver.GrabExhibitOutcome();
        _hub.Project(driver, starting, concealed);
        cycle = new SimAvatarPresentationFrame(starting, concealed, AdvancedBeforeNetwork: false);
        return true;
    }

    // The controller when the player may be advanced this frame, else null
    private AvatarLocomotionDriver? Live()
    {
        var driver = _hub.Controller;
        return _hub.CanAdvancePlayer && driver is { CanPerformOnlineTravel: true } ? driver : null;
    }

    // Projects the controller's current presentation without stepping it and records a non-advanced
    // half frame
    private void Grip(AvatarLocomotionDriver driver, bool concealed)
    {
        var still = driver.GrabExhibitOutcome();
        _hub.Project(driver, still, concealed);
        _half = new HalfFrame(driver, still, ObjectAdvanced: false, concealed, ObjectQuantumAdvanced: false);
    }

    // The half-frame result with the spatial fields re-read from the controller, which the network may
    // have moved
    private static LocomotionResult Refreshed(LocomotionResult travel, AvatarLocomotionDriver driver)
    {
        return travel with
        {
            Position = driver.Position,
            RenderPosition = driver.RasterizeLocus,
            CellId = driver.CellId,
            IsOnGround = driver.CanTransmitLocusSignal,
        };
    }
}
