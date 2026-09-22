using MacAC.Mechanics.Gear;

namespace MacAC.Client.Realm;

public sealed class ExternalContainerLifespanDriver : IDisposable
{
    private readonly OpenContainerState _phase;
    private readonly ClientThingChart _objects;
    private readonly Action<uint> _transmitNoLongerViewingInsides;
    private bool _destroyed;

    public ExternalContainerLifespanDriver(
        OpenContainerState state,
        ClientThingChart objects,
        Action<uint> sendNoLongerViewingContents)
    {
        _phase = state ?? throw new ArgumentNullException(nameof(state));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _transmitNoLongerViewingInsides = sendNoLongerViewingContents
            ?? throw new ArgumentNullException(nameof(sendNoLongerViewingContents));
        _phase.Changed += OnAltered;
    }

    private void OnAltered(OpenContainerShift changeover)
    {
        if (changeover.PreviousContainerId is not 0u)
            _objects.HaltViewingInsidesTree(changeover.PreviousContainerId);

        if (changeover.Kind == OpenContainerShiftKind.ReplacementRequested
            && changeover.PreviousContainerId is not 0u)
        {
            _transmitNoLongerViewingInsides(changeover.PreviousContainerId);
        }
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _phase.Changed -= OnAltered;
    }
}
