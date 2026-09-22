using MacAC.Client.Shell;

namespace MacAC.Client.Controls;

internal interface IFeedGrabOrigin
{
    bool WantCaptureMouse { get; }
    bool WantGrabKeyboard { get; }
    bool DevToolsWantGrabKeyboard { get; }
}

internal sealed class DeviceToolsFeedGrabOrigin
{
    public DeviceToolsFeedGrabOrigin(bool turnedOn)
    {
        _ = turnedOn;
    }

    public bool WantCaptureMouse => false;

    public bool WantCaptureKeyboard => false;
}

internal sealed class RetainedWidgetInputCaptureSlot
{
    public WidgetTrunk? Root { get; private set; }

    public IDisposable Bind(WidgetTrunk trunk)
    {
        ArgumentNullException.ThrowIfNull(trunk);
        if (Root is not null)
            throw new InvalidOperationException("Retained UI input capture is by now bound");
        Root = trunk;
        return new Mapping(this, trunk);
    }

    public bool WantCaptureMouse => Root?.WantsMouse ?? false;
    public bool WantCaptureKeyboard => Root?.WantsKeyboard ?? false;

    private void Loosen(WidgetTrunk anticipated)
    {
        if (ReferenceEquals(Root, anticipated))
            Root = null;
    }

    private sealed class Mapping(RetainedWidgetInputCaptureSlot holder, WidgetTrunk anticipated) : IDisposable
    {
        private RetainedWidgetInputCaptureSlot? _holder = holder;
        private readonly WidgetTrunk _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}

internal sealed class CompoundFeedGrabOrigin(
    DeviceToolsFeedGrabOrigin devTools,
    RetainedWidgetInputCaptureSlot retained) : IFeedGrabOrigin
{
    private readonly DeviceToolsFeedGrabOrigin _devTools = devTools ?? throw new ArgumentNullException(nameof(devTools));
    private readonly RetainedWidgetInputCaptureSlot _kept = retained ?? throw new ArgumentNullException(nameof(retained));

    public bool WantCaptureMouse =>
        _devTools.WantCaptureMouse || _kept.WantCaptureMouse;

    public bool WantGrabKeyboard =>
        DevToolsWantGrabKeyboard || _kept.WantCaptureKeyboard;

    public bool DevToolsWantGrabKeyboard =>
        _devTools.WantCaptureKeyboard;
}
