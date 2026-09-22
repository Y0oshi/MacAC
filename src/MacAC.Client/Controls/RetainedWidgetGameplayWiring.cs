using MacAC.Client.Graphics;
using MacAC.Client.Shell;

namespace MacAC.Client.Controls;

internal interface IRetainedWidgetDragReleaseSurface
{
    void AppendReleasedBeyond(Action<object, int, int> hook);

    void DropReleasedBeyond(Action<object, int, int> hook);
}

internal sealed class WidgetRootDragReleaseSurface(WidgetTrunk root)
    : IRetainedWidgetDragReleaseSurface
{
    private readonly WidgetTrunk _trunk = root
        ?? throw new ArgumentNullException(nameof(root));

    public void AppendReleasedBeyond(Action<object, int, int> hook) =>
        _trunk.DragReleasedOutsideUi += hook;

    public void DropReleasedBeyond(Action<object, int, int> hook) =>
        _trunk.DragReleasedOutsideUi -= hook;
}

internal sealed class RetainedWidgetGameplayWiring : IDisposable
{
    private readonly IRetainedWidgetDragReleaseSurface _canvas;
    private readonly Action<GearDragPayload, int, int> _placeDraggedGear;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly Action<object, int, int> _releasedBeyond;
    private AssetShutdownTransaction? _unfasten;
    private bool _affixed;
    private bool _fastenBegun;
    private int _teardownAsked;
    private int _engaged;

    public RetainedWidgetGameplayWiring(
        IRetainedWidgetDragReleaseSurface surface,
        Action<GearDragPayload, int, int> placeDraggedItem,
        HostQuiescenceTurnstile quiescence)
    {
        _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
        _placeDraggedGear = placeDraggedItem
            ?? throw new ArgumentNullException(nameof(placeDraggedItem));
        _stillness = quiescence
            ?? throw new ArgumentNullException(nameof(quiescence));
        _releasedBeyond = OnReleasedBeyond;
    }

    public static RetainedWidgetGameplayWiring Create(
        WidgetTrunk trunk,
        Action<GearDragPayload, int, int> placeDraggedGear,
        HostQuiescenceTurnstile stillness)
    {
        return new(new WidgetRootDragReleaseSurface(trunk), placeDraggedGear, stillness);
    }

    public bool IsDisposalComplete => !_affixed;

    public void Affix()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
        if (_fastenBegun)
        {
            throw new InvalidOperationException(
                "Retained gameplay attachment has by now started");
        }

        _fastenBegun = true;
        try
        {
            _affixed = true;
            _canvas.AppendReleasedBeyond(_releasedBeyond);
            Volatile.Write(ref _engaged, 1);
        }
        catch (Exception fastenProblem)
        {
            Deactivate();
            try
            {
                SecureUnfastenTransaction().CompleteOrThrow();
            }
            catch (Exception undoProblem)
            {
                throw new AggregateException(
                    "Retained gameplay registration and rollback both failed",
                    new InvalidOperationException(
                        "Retained gameplay registration failed", fastenProblem),
                    undoProblem);
            }

            throw new InvalidOperationException(
                "Retained gameplay registration failed and was rolled back",
                fastenProblem);
        }
    }

    public void Deactivate() => Interlocked.Exchange(ref _engaged, 0);

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Deactivate();
        SecureUnfastenTransaction().CompleteOrThrow();
    }

    private void OnReleasedBeyond(object cargo, int x, int y)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0
                && cargo is GearDragPayload gear)

                _placeDraggedGear(gear, x, y);
        });
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        return _unfasten ??= new AssetShutdownTransaction(
            new AssetShutdownJuncture("retained gameplay callbacks",
            [
                new("drag released outside UI", Drop),
            ]));
    }

    private void Drop()
    {
        if (!_affixed)
            return;

        _canvas.DropReleasedBeyond(_releasedBeyond);
        _affixed = false;
    }
}
