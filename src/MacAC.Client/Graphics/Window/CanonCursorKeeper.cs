using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Shell;
using MacAC.Mechanics.Surfaces;
using Silk.NET.Core;
using Silk.NET.Input;

namespace MacAC.Client.Graphics;

public sealed class CanonCursorKeeper(IDatAccess datFiles, object datMutex)
{
    private readonly IDatAccess _datFiles = datFiles;
    private readonly object _datMutex = datMutex;
    private readonly CanonCursorPicker _globalCursors = new CanonCursorPicker(datFiles, datMutex);
    private readonly Dictionary<uint, RawImage> _imagesByCanvas = [];
    private readonly HashSet<uint> _absentCanvases = [];
    private readonly HashSet<CanonGlobalCursorKind> _reportedBackups = [];
    private bool _hasStratumPhase;
    private CanonGlobalCursorKind _previousGlobalSort;
    private WidgetCursorMedia _previousWidgetCur;
    private WidgetCursorMedia _previousImposedCur;
    private StandardCursor? _previousStandardCur;
    private GlfwCursorShelf? _nativeCursors;

    public void FastenNativePane(nint glfwPaneHnd)
    {
        if (_nativeCursors is null)
        {
            _nativeCursors = GlfwCursorShelf.TryBuild(glfwPaneHnd);
            if (_nativeCursors is not null)
                Console.WriteLine("[UI] cursor native cache attached");
        }
    }

    public void Apply(IEnumerable<IMouse> mice, ClientCursorFeedback feedback)
    {
        foreach (CanonCursorLayer stratum in PlanApplication(
            _hasStratumPhase,
            _previousGlobalSort,
            _previousWidgetCur,
            feedback.GlobalKind,
            feedback.Cursor))
        {
            if (stratum == CanonCursorLayer.Widget
                && feedback.Cursor.IsValid
                && TryFetchImage(feedback.Cursor.File, out var image))
            {
                ImposeCustom(mice, feedback.Cursor, image);
            }
            else
            {
                ImposeGlobal(mice, feedback.GlobalKind);
            }
        }

        _hasStratumPhase = true;
        _previousGlobalSort = feedback.GlobalKind;
        _previousWidgetCur = feedback.Cursor;
    }

    internal static IReadOnlyList<CanonCursorLayer> PlanApplication(
        bool hasStratumPhase,
        CanonGlobalCursorKind earlierGlobal,
        WidgetCursorMedia earlierWidget,
        CanonGlobalCursorKind latestGlobal,
        WidgetCursorMedia latestWidget)
    {
        bool globalAltered = !hasStratumPhase || earlierGlobal != latestGlobal;
        bool widgetAltered = !hasStratumPhase || earlierWidget != latestWidget;
        if (!globalAltered && !widgetAltered)
            return Array.Empty<CanonCursorLayer>();

        var outcome = new List<CanonCursorLayer>(2);
        if (globalAltered)
            outcome.Add(CanonCursorLayer.Global);

        if (widgetAltered)
        {
            CanonCursorLayer widgetOutcome = latestWidget.IsValid
                ? CanonCursorLayer.Widget
                : CanonCursorLayer.Global;
            if (outcome.Count is 0 || outcome[^1] != widgetOutcome)
                outcome.Add(widgetOutcome);
        }

        return outcome;
    }

    private void ImposeGlobal(IEnumerable<IMouse> mice, CanonGlobalCursorKind sort)
    {
        if (_globalCursors.TryResolve(sort, out var globalCur)
            && TryFetchImage(globalCur.File, out var globalImage))
        {
            ImposeCustom(mice, globalCur, globalImage);
            return;
        }

        if (_reportedBackups.Add(sort))
        {
            Console.Error.WriteLine(
                $"[UI] retail cursor {sort} could not be resolved from DAT; " +
                "using the registered OS cursor adaptation");
        }
        ImposeStandard(mice, StandardCurFor(sort));
    }

    private void ImposeCustom(IEnumerable<IMouse> mice, WidgetCursorMedia curMedia, RawImage image)
    {
        if (_previousStandardCur is null && _previousImposedCur.Equals(curMedia))
            return;

        if (_nativeCursors is not null && _nativeCursors.TrySetCustom(curMedia, image))
        {
            _previousImposedCur = curMedia;
            _previousStandardCur = null;
            return;
        }

        foreach (var pointer in mice)
        {
            ICursor cur = pointer.Cursor;
            cur.Image = image;
            cur.HotspotX = curMedia.HotspotX;
            cur.HotspotY = curMedia.HotspotY;
            if (cur.Type != CursorType.Custom)
                cur.Type = CursorType.Custom;
        }

        _previousImposedCur = curMedia;
        _previousStandardCur = null;
    }

    private void ImposeStandard(IEnumerable<IMouse> mice, StandardCursor wanted)
    {
        if (_previousStandardCur == wanted)
            return;

        if (_nativeCursors is not null && _nativeCursors.TrySetStandard(wanted))
        {
            _previousImposedCur = default;
            _previousStandardCur = wanted;
            return;
        }

        foreach (var pointer in mice)
        {
            ICursor cur = pointer.Cursor;
            var standard = wanted;
            if (!cur.IsSupported(standard))
                standard = StandardCursor.Arrow;
            if (!cur.IsSupported(standard))
                continue;

            if (cur.Type != CursorType.Standard)
                cur.Type = CursorType.Standard;
            if (cur.StandardCursor != standard)
                cur.StandardCursor = standard;
        }

        _previousImposedCur = default;
        _previousStandardCur = wanted;
    }

    private bool TryFetchImage(uint rasterizeCanvasIdent, out RawImage image)
    {
        if (_imagesByCanvas.TryGetValue(rasterizeCanvasIdent, out image))
            return true;
        if (_absentCanvases.Contains(rasterizeCanvasIdent))
            return false;

        var decoded = UnpackCurCanvas(rasterizeCanvasIdent);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0 || decoded.Rgba8.Length is 0)
        {
            _absentCanvases.Add(rasterizeCanvasIdent);
            image = default;
            return false;
        }

        image = new RawImage(decoded.Width, decoded.Height, decoded.Rgba8);
        _imagesByCanvas[rasterizeCanvasIdent] = image;
        return true;
    }

    private UnpackedTexture? UnpackCurCanvas(uint rasterizeCanvasIdent)
    {
        lock (_datMutex)
        {
            if (!_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out var surface)
                && !_datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out surface))
                return null;

            ColorTable? swatch = surface.DefaultColorTableId is not 0
                ? _datFiles.Get<ColorTable>(surface.DefaultColorTableId)
                : null;
            return CanvasUnpacker.DecodeRenderSurface(surface, swatch);
        }
    }

    private static StandardCursor StandardCurFor(CanonGlobalCursorKind sort)
    {
        return sort switch
        {
            CanonGlobalCursorKind.Use or CanonGlobalCursorKind.UseFound => StandardCursor.Hand,
            CanonGlobalCursorKind.TargetPending => StandardCursor.Crosshair,
            CanonGlobalCursorKind.TargetValid => StandardCursor.ResizeAll,
            CanonGlobalCursorKind.TargetInvalid => StandardCursor.NotAllowed,
            _ => StandardCursor.Arrow,
        };
    }
}

internal enum CanonCursorLayer
{
    Global,
    Widget,
}
