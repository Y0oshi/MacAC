namespace MacAC.Client.Shell;

public enum WidgetMarkupListColumnKind
{
    Text,
    Check,
    Icon,
}

public sealed class WidgetMarkupListColumn
{
    public WidgetMarkupListColumnKind Kind { get; internal init; }
    public float Width { get; internal init; }
    public bool IsAutoWidth { get; internal init; }

    public Func<IReadOnlyList<string>>? PhraseSource { get; internal init; }
    public Func<IReadOnlyList<uint>>? TintsSrc { get; internal init; }
    public Action<int>? PhraseClicked { get; internal init; }

    public Func<IReadOnlyList<bool>>? VerifySrc { get; internal init; }
    public Action<int>? VerifyAltered { get; internal init; }

    public Func<IReadOnlyList<uint>>? GlyphValsSrc { get; internal init; }
    public Func<uint, (uint tex, int w, int h)>? GlyphResolve { get; internal init; }
    public Action<int>? GlyphClicked { get; internal init; }

    public static WidgetMarkupListColumn Text(
        float width,
        Func<IReadOnlyList<string>> phraseSrc,
        Func<IReadOnlyList<uint>>? tintsSrc,
        Action<int>? onPress = null,
        bool isAutoWidth = false)
    {
        return new()
        {
            Kind = WidgetMarkupListColumnKind.Text,
            Width = width,
            IsAutoWidth = isAutoWidth,
            PhraseSource = phraseSrc,
            TintsSrc = tintsSrc,
            PhraseClicked = onPress,
        };
    }

    public static WidgetMarkupListColumn Check(
        float width,
        Func<IReadOnlyList<bool>> verifySrc,
        Action<int> onEdit,
        bool isAutoWidth = false)
    {
        return new()
        {
            Kind = WidgetMarkupListColumnKind.Check,
            Width = width,
            IsAutoWidth = isAutoWidth,
            VerifySrc = verifySrc,
            VerifyAltered = onEdit,
        };
    }

    public static WidgetMarkupListColumn Icon(
        float width,
        Func<IReadOnlyList<uint>> valsSrc,
        Func<uint, (uint tex, int w, int h)>? locate,
        Action<int> onPress,
        bool isAutoWidth = false)
    {
        return new()
        {
            Kind = WidgetMarkupListColumnKind.Icon,
            Width = width,
            IsAutoWidth = isAutoWidth,
            GlyphValsSrc = valsSrc,
            GlyphResolve = locate,
            GlyphClicked = onPress,
        };
    }

    public int RankCount()
    {
        return Kind switch
        {
            WidgetMarkupListColumnKind.Text => PhraseSource?.Invoke().Count ?? 0,
            WidgetMarkupListColumnKind.Check => VerifySrc?.Invoke().Count ?? 0,
            WidgetMarkupListColumnKind.Icon => GlyphValsSrc?.Invoke().Count ?? 0,
            _ => 0,
        };
    }
}
