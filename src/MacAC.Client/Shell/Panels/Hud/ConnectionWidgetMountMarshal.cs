using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

internal sealed class ConnectionWidgetMountMarshal(
    WidgetTrunk hub, CanonWidgetAssets holdings, ConnectionEngineWiring mappings) : IDisposable
{
    private ConnectionWidgetDriver? _driver;
    private bool _destroyed;

    internal bool IsShown => _driver?.Root.Visible == true;

    internal void Tick()
    {
        if (_destroyed) return;
        if (_driver is null)
        {
            lock (holdings.DatLock)
            {
                uint arrangementIdent = CanonDataIdResolver.Resolve(holdings.Dats,
                    ConnectionWidgetDriver.TrunkEnum, 5u);
                ImportedArrangement? arrangement = arrangementIdent is 0u ? null : ArrangementLoader.Import(
                    holdings.Dats, arrangementIdent, ConnectionWidgetDriver.TrunkElemIdent,
                    holdings.ResolveSprite, holdings.DefaultFont, holdings.ResolveFont);
                if (arrangement is null) return;
                DatStringPicker texts = new DatStringPicker(holdings.Dats);
                string? checking = texts.Resolve(0x23000002u,
                    DatStringPicker.CalculateDigest("ID_DataPatch_Interrogation"));
                string? done = texts.Resolve(0x23000002u,
                    DatStringPicker.CalculateDigest("ID_DataPatch_PatchingDone"));
                if (checking is null || done is null) return;
                _driver = ConnectionWidgetDriver.Bind(hub, arrangement, mappings,
                    checking, done);
            }
        }
        _driver?.Tick();
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _driver?.Dispose();
        _driver = null;
    }
}
