using MacAC.Client.Graphics;

namespace MacAC.Client.Shell.Panels;

internal sealed record ToonManagementWidgetMountResources(
    uint LayoutId,
    ImportedArrangement Layout,
    Func<uint, uint, WidgetElem?> TemplateResolver,
    ToonManagementWidgetDriver.PromptStrings Strings,
    BitmapFont? VersionFont = null);

internal sealed class ToonManagementWidgetMountMarshal(
    WidgetTrunk host,
    ToonPickingEngineWiring bindings,
    Func<CanonPromptMint?> ensureDialogs,
    Func<ToonManagementWidgetMountResources?> loadResources,
    Action? openCredits = null) : IDisposable
{
    private readonly WidgetTrunk _hub = host ?? throw new ArgumentNullException(nameof(host));
    private readonly ToonPickingEngineWiring _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    private readonly Func<CanonPromptMint?> _securePopups = ensureDialogs
            ?? throw new ArgumentNullException(nameof(ensureDialogs));
    private readonly Func<ToonManagementWidgetMountResources?> _pullAssetList = loadResources
            ?? throw new ArgumentNullException(nameof(loadResources));
    private readonly Action? _openCredits = openCredits;
    private bool _destroyed;

    public ToonManagementWidgetDriver? Controller { get; private set; }

    public void Tick()
    {
        if (_destroyed || Controller is not null)
            return;

        try
        {
            var popups = _securePopups();
            if (popups is null)
                return;

            var assetList = _pullAssetList();
            if (assetList is null)
                return;

            var contender =
                ToonManagementWidgetDriver.BuildDetached(
                _hub,
                assetList.Layout,
                assetList.TemplateResolver,
                    popups,
                    _bindings,
                    assetList.Strings,
                    _openCredits,
                    verTypeface: assetList.VersionFont);
            if (contender is null)
                return;

            Controller = contender;
            contender.FastenAndBeat();
            Console.WriteLine(
                $"[UI] retail character management from enum table 5 "
                + $"(0x10000005 -> 0x{assetList.LayoutId:X8}, "
                + "root 0x1000039A; flat list, no viewport)");
        }
        catch (Exception problem)
        {
            var partial = Controller;
            Controller = null;
            try
            {
                partial?.Dispose();
            }
            catch (Exception tidyProblem)
            {
                Console.WriteLine(
                    "[UI] character management partial-mount cleanup failed: "
                    + tidyProblem.Message);
            }
            Console.WriteLine(
                "[UI] character management mount will retry after resource "
                + $"recovery: {problem.Message}");
        }
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        Controller?.Dispose();
        Controller = null;
    }
}
