using MacAC.Mechanics.Arcana;

namespace MacAC.Client.Shell.Panels;

public sealed partial class AssayWidgetDriver
{

    public void OnShown() => _paneShown = true;

    public void OnConcealed() => _paneShown = false;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _pick.Changed -= ProcessPickAltered;
        _objects.ObjectAdded -= ProcessArcanumModuleObjectAltered;
        _objects.ObjectMoved -= ProcessArcanumModuleObjectMoved;
        _objects.ObjectRemoved -= ProcessArcanumModuleObjectAltered;
        _objects.StackSizeUpdated -= ProcessArcanumModuleObjectAltered;
        _objects.Cleared -= ProcessArcanumModuleObjectsCleared;
        _shut?.OnClick = null;
        if (_inscriptionField is not null)
        {
            _inscriptionField.OnFocusGained = null;
            _inscriptionField.OnFocusLost = null;
            _inscriptionField.OnScanSolePress = null;
        }
        _inscriptionBackground?.OnClick = null;
        WipeArcanumEquation();
        _arcanumGlyphHub.DropDescendant(_arcanumGlyph);
        foreach (WidgetScroller scroller in Descendants(_arrangement.Root).OfType<WidgetScroller>())
            if (ReferenceEquals(scroller.Model, _gearPhrase.Scroll)
                || (_inscriptionPhrase is not null
                    && ReferenceEquals(scroller.Model, _inscriptionPhrase.Scroll))
                || (_inscriptionField is not null
                    && ReferenceEquals(scroller.Model, _inscriptionField.Scroll)))
                scroller.Model = null;
    }
    private static IEnumerable<WidgetElem> Descendants(WidgetElem? trunk)
    {
        if (trunk is null)
            yield break;
        foreach (WidgetElem child in trunk.Children)
        {
            yield return child;
            foreach (WidgetElem descendant in Descendants(child))
                yield return descendant;
        }
    }

    private void RenewArcanumModules()
    {
        if (EngagedLens != AssayView.Spell
            || _arcanumIdent is 0u
            || !_grimoire.TryFetchMetadata(_arcanumIdent, out SpellMeta metadata))
            return;

        var modules =
            _arcanumModules(_arcanumIdent);
        AssignArcanumPhrase(_arcanumReadout, AssembleArcanumReadout(metadata, modules));
        ReassembleArcanumEquation(modules);
    }
}
