namespace MacAC.Client.Shell.Panels;

public sealed class ImportedArrangement(WidgetElem trunk, Dictionary<uint, WidgetElem> byIdent)
{
    public WidgetElem Root { get; } = trunk;

    private readonly Dictionary<uint, WidgetElem> _byIdent = byIdent;

    public WidgetElem? SeekElem(uint ident)
        => _byIdent.TryGetValue(ident, out var element) ? element : null;
}

public static partial class ArrangementLoader
{

}
