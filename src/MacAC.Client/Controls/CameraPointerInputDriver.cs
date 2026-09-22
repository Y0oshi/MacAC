using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Cockpit.Input;
using Silk.NET.Input;

namespace MacAC.Client.Controls;

internal interface ICrudePtrCanvas
{
    void AppendPointerRelocate(Action<Vector2> hook);
    void DropPointerRelocate(Action<Vector2> hook);
}

internal sealed class SilkCrudePtrCanvas : ICrudePtrCanvas
{
    private readonly IMouse _pointer;
    private Action<Vector2>? _hook;
    private readonly Action<IMouse, Vector2> _pointerRelocate;

    public SilkCrudePtrCanvas(IMouse mouse)
    {
        _pointer = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _pointerRelocate = OnPointerRelocate;
    }

    public void AppendPointerRelocate(Action<Vector2> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        if (_hook is not null)
            throw new InvalidOperationException("The raw mouse surface is by now attached");

        _hook = hook;
        _pointer.MouseMove += _pointerRelocate;
    }

    public void DropPointerRelocate(Action<Vector2> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        if (!ReferenceEquals(_hook, hook))
            return;

        _pointer.MouseMove -= _pointerRelocate;
        _hook = null;
    }

    private void OnPointerRelocate(IMouse _, Vector2 locus) =>
        _hook?.Invoke(locus);
}

internal interface IPtrCursorMannerMark
{
    CursorMode CurManner { get; set; }
}

internal sealed class SilkPtrCursorMannerMark(IMouse mouse)
    : IPtrCursorMannerMark
{
    private readonly IMouse _pointer = mouse
        ?? throw new ArgumentNullException(nameof(mouse));

    public CursorMode CurManner
    {
        get => _pointer.Cursor.CursorMode;
        set => _pointer.Cursor.CursorMode = value;
    }
}

internal interface IPointerSensitivityDirectives
{
    string TuneSensitivity(float factor);
}

internal sealed partial class CameraPointerInputDriver
    : IDisposable, IPointerSensitivityDirectives
{
    private readonly IReadOnlyList<ICrudePtrCanvas> _canvases;

    private readonly IPtrCursorMannerMark _cur;

    private readonly HostQuiescenceTurnstile _stillness;

    private readonly IFeedGrabOrigin _grab;

    private readonly AvatarModeLedger _avatarManner;

    private readonly CameraDriver _cam;

    private readonly FollowCameraInputLedger _pursue;

    private readonly IMouseFeed _mouse;

    private readonly PointerPositionLedger _ptr;

    private readonly IFeedMonotonicTimer _clock;

    private readonly Action<Vector2> _pointerMoved;

    private readonly Action<bool> _camMannerAltered;

    private readonly bool[] _canvasAffixed;

    private AssetShutdownTransaction? _unfasten;

    private GameplayInputFrameDriver? _gameplayCycle;

    private bool _camAffixed;

    private bool _fastenBegun;

    private int _teardownAsked;

    private int _engaged;

    private float _flySensitivity = 1f;

    private float _orbitSensitivity = 1f;

    public CameraPointerInputDriver(
        IReadOnlyList<ICrudePtrCanvas> surfaces,
        IPtrCursorMannerMark cursor,
        HostQuiescenceTurnstile quiescence,
        IFeedGrabOrigin capture,
        AvatarModeLedger playerMode,
        CameraDriver camera,
        FollowCameraInputLedger chase,
        IMouseFeed mouse,
        PointerPositionLedger pointer,
        IFeedMonotonicTimer clock)
    {
        _canvases = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        if (_canvases.Any(static canvas => canvas is null))
            throw new ArgumentException("Raw pointer surfaces can't contain null", nameof(surfaces));
        _cur = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        _grab = capture ?? throw new ArgumentNullException(nameof(capture));
        _avatarManner = playerMode ?? throw new ArgumentNullException(nameof(playerMode));
        _cam = camera ?? throw new ArgumentNullException(nameof(camera));
        _pursue = chase ?? throw new ArgumentNullException(nameof(chase));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _ptr = pointer ?? throw new ArgumentNullException(nameof(pointer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _canvasAffixed = new bool[_canvases.Count];
        _pointerMoved = OnPointerMoved;
        _camMannerAltered = OnCamMannerAltered;
    }

    private sealed class GameplayFrameWiring(
        CameraPointerInputDriver holder,
        GameplayInputFrameDriver anticipated) : IDisposable
    {
        private CameraPointerInputDriver? _holder = holder;
        private readonly GameplayInputFrameDriver _anticipated = anticipated;

        public void Dispose()
        {
            Interlocked.Exchange(ref _holder, null)?
                .LoosenGameplayCycle(_anticipated);
        }
    }
}
