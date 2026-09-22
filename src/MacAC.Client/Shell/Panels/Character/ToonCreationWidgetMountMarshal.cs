namespace MacAC.Client.Shell.Panels;

internal sealed record ToonCreationWidgetMountResources(
    uint LayoutId,
    ImportedArrangement Layout,
    Func<uint, uint, WidgetElem?> TemplateResolver,
    ToonCreationWidgetDriver.PromptStrings Strings);

internal sealed class ToonCreationWidgetMountMarshal(
    WidgetTrunk host,
    ToonCreationEngineWiring bindings,
    Func<CanonPromptMint?> ensureDialogs,
    Func<ToonCreationWidgetMountResources?> loadResources) : IDisposable
{
    private readonly WidgetTrunk _hub = host ?? throw new ArgumentNullException(nameof(host));
    private readonly ToonCreationEngineWiring _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    private readonly Func<CanonPromptMint?> _securePopups = ensureDialogs
            ?? throw new ArgumentNullException(nameof(ensureDialogs));
    private readonly Func<ToonCreationWidgetMountResources?> _pullAssetList = loadResources
            ?? throw new ArgumentNullException(nameof(loadResources));
    private bool _destroyed;

    public ToonCreationWidgetDriver? Controller { get; private set; }

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
                ToonCreationWidgetDriver.BuildDetached(
                    _hub,
                    assetList.Layout,
                    assetList.TemplateResolver,
                    popups,
                    _bindings,
                    assetList.Strings);
            if (contender is null)
                return;

            Controller = contender;
            contender.FastenAndBeat();
            Console.WriteLine(
                $"[UI] retail character creation from enum table 5 "
                + $"(0x10000039 -> 0x{assetList.LayoutId:X8}, root 0x100003CC)");
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
                    "[UI] character creation partial-mount cleanup failed: "
                    + tidyProblem.Message);
            }
            Console.WriteLine(
                "[UI] character creation mount will retry after resource "
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
