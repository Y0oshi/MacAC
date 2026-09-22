using MacAC.Client.Preferences;
using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Graphics.Packs;

internal sealed class RenderPackPickingWiring : IDisposable
{
    private readonly EnginePreferencesDriver _prefs;
    private readonly RenderPackDriver _driver;
    private readonly Action<string> _trace;
    private long _backupPersistedGen = -1;
    private bool _suppressReadoutRim;
    private bool _destroyed;

    internal RenderPackPickingWiring(
        EnginePreferencesDriver settings,
        RenderPackDriver controller,
        Action<string>? trace = null)
    {
        _prefs = settings ?? throw new ArgumentNullException(nameof(settings));
        _driver = controller ?? throw new ArgumentNullException(nameof(controller));
        _trace = trace ?? (_ => { });
        _prefs.DisplayChanged += OnReadoutAltered;
        _driver.Request(_prefs.Readout.RenderPack);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _prefs.DisplayChanged -= OnReadoutAltered;
    }

    internal RenderPackActivationCapture ImposeAtCycleBoundary(
        RasterizeBundleActivationReach reach)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        var capture = _driver.ImposeAtCycleBoundary(reach);
        if (capture.State != RenderPackActivationPhase.FailedToRetail
            || capture.ActivationGeneration == _backupPersistedGen
            || _prefs.Readout.RenderPack.IsCanon)
            return capture;

        _backupPersistedGen = capture.ActivationGeneration;
        _suppressReadoutRim = true;
        try
        {
            _prefs.StoreReadout(_prefs.Readout with
            {
                RenderPack = RenderPackPick.Retail,
            });
        }
        finally
        {
            _suppressReadoutRim = false;
        }

        if (_prefs.Readout.RenderPack.IsCanon)
        {
            _trace(
                $"[render-pack] selection failed; persisted macac default (retail-faithful): "
                + capture.Reason);
        }
        else
        {
            _trace(
                $"[render-pack] selection failed and retail fallback could not be persisted: "
                + capture.Reason);
        }
        return capture;
    }

    private void OnReadoutAltered(ReadoutPrefs readout)
    {
        if (!_destroyed && !_suppressReadoutRim)
            _driver.Request(readout.RenderPack, explicitUserChoice: true);
    }
}
