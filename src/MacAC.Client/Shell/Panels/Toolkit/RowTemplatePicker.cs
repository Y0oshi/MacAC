namespace MacAC.Client.Shell.Panels;

public sealed class RowTemplatePicker(
    Func<uint, uint, ElemDetails?> importInfos,
    Func<ElemDetails, WidgetElem?> build)
{
    private readonly Dictionary<(uint LayoutId, uint ElementId), ElemDetails?> _stash = [];
    private readonly Func<uint, uint, ElemDetails?> _importInfos = importInfos ?? throw new ArgumentNullException(nameof(importInfos));
    private readonly Func<ElemDetails, WidgetElem?> _assemble = build ?? throw new ArgumentNullException(nameof(build));

    public int ImportTally { get; private set; }

    public WidgetElem? Resolve(uint blueprintArrangementIdent, uint blueprintElemIdent)
    {
        var tag = (templateLayoutId: blueprintArrangementIdent, templateElementId: blueprintElemIdent);
        if (!_stash.TryGetValue(tag, out ElemDetails? details))
        {
            details = _importInfos(blueprintArrangementIdent, blueprintElemIdent);
            _stash[tag] = details;
            ++ImportTally;
        }
        return details is null ? null : _assemble(details);
    }

    public ElemDetails? LocateDetails(uint blueprintArrangementIdent, uint blueprintElemIdent)
    {
        var tag = (templateLayoutId: blueprintArrangementIdent, templateElementId: blueprintElemIdent);
        if (!_stash.TryGetValue(tag, out ElemDetails? details))
        {
            details = _importInfos(blueprintArrangementIdent, blueprintElemIdent);
            _stash[tag] = details;
            ++ImportTally;
        }
        return details;
    }
}
