using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public class WidgetDatElement : WidgetElem, IWidgetDatStateful
{
    // DAT paint modes: 0 undefined, 1 normal, 2 overlay, 3 alpha blend

    protected readonly ElemDetails Info;
    private readonly Func<uint, (uint tex, int w, int h)> _locate;

    public uint ElementId => Info.Id;

    /// <summary>Which state name to render. <c>""</c> = the unnamed DirectState.</summary>
    public string EngagedPhase { get; set; } = "";

    public uint EngagedCanonPhaseIdent
    {
        get
        {
            if (string.IsNullOrEmpty(EngagedPhase))
                return WidgetStateInfo.StraightPhaseIdent;
            foreach (var (ident, phase) in Info.States)
                if (string.Equals(phase.Name, EngagedPhase, StringComparison.Ordinal))
                    return ident;
            return WidgetButtonStateMachine.TryPhaseTag(EngagedPhase, out uint standard)
                ? standard
                : CanonWidgetStateIds.TryPhaseIdent(EngagedPhase, out uint custom) ? custom : 0u;
        }
    }

    public override string EngagedCurPhaseLabel => EngagedPhase;

    public bool TrySetCanonPhase(uint phaseIdent)
    {
        uint imposedPhaseIdent = phaseIdent;
        WidgetStateInfo? chosenPhase = null;
        if (phaseIdent == WidgetStateInfo.StraightPhaseIdent)
        {
            if (!Info.States.TryGetValue(phaseIdent, out chosenPhase)
                && !Info.StateMedia.ContainsKey(""))
                return false;
            EngagedPhase = "";
        }
        else if (Info.States.TryGetValue(phaseIdent, out chosenPhase))
        {
            EngagedPhase = chosenPhase.Name;
        }
        else
        {
            string phaseLabel = WidgetButtonStateMachine.PhaseMoniker(phaseIdent);
            if (string.IsNullOrEmpty(phaseLabel))
                phaseLabel = CanonWidgetStateIds.PhaseLabel(phaseIdent);
            if (string.IsNullOrEmpty(phaseLabel) || !Info.StateMedia.ContainsKey(phaseLabel))
            {
                EngagedPhase = "";
                imposedPhaseIdent = WidgetStateInfo.StraightPhaseIdent;
                Info.States.TryGetValue(imposedPhaseIdent, out chosenPhase);
            }
            else
            {
                EngagedPhase = phaseLabel;
            }
        }

        if (chosenPhase is not null
            && chosenPhase.Properties.TryGetValue(0x3Bu, out var invisibleProp)
            && invisibleProp.Kind == WidgetPropertyKind.Bool)
            Visible = !invisibleProp.BoolValue;

        if (chosenPhase?.PassToChildren == true)
        {
            foreach (WidgetElem descendant in Children)
                if (descendant is IWidgetDatStateful stateful)
                    stateful.TrySetCanonPhase(imposedPhaseIdent);
        }
        return true;
    }

    public WidgetDatElement(ElemDetails details, Func<uint, (uint tex, int w, int h)> locate)
    {
        Info = details;
        _locate = locate;
        ClickThrough = true; // generic decoration; behavioral widgets opt back in

        if (!string.IsNullOrEmpty(details.DefaultStateName))
            EngagedPhase = details.DefaultStateName;
        else if (details.StateMedia.ContainsKey("Normal"))
            EngagedPhase = "Normal";

        Outline = details.Outline;
        if (details.OutlineColor.HasValue)
            OutlineColor = details.OutlineColor.Value;
    }

    public (uint File, int DrawMode) EngagedMedia()
        => Info.StateMedia.TryGetValue(EngagedPhase, out var m) ? m
         : Info.StateMedia.TryGetValue("", out var d) ? d
         : (0u, 0);

    public Action? OnClick { get; set; }
    public Action<int, int>? OnPressAt { get; set; }

    public override bool HndsPress => OnClick is not null || OnPressAt is not null;

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.Click && (OnClick is not null || OnPressAt is not null))
        {
            OnClick?.Invoke();
            OnPressAt?.Invoke(e.Data1, e.Data2);
            return true;
        }
        return false;
    }

    public string? Label { get; set; }
    public WidgetDatFont? LabelFont { get; set; }
    public Vector4 CaptionTint { get; set; } = Vector4.One;

    public Vector4 Tint { get; set; } = Vector4.One;

    public bool Outline { get; set; }

    public Vector4 OutlineColor { get; set; } = WidgetRenderScope.DefaultOutlineTint;

    public bool MediaShown { get; set; } = true;

    public uint? CoreImageTexture { get; set; }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (MediaShown && CoreImageTexture is uint coreTexture)
        {
            if (coreTexture != 0u)
            {
                cx.SketchSprite(
                    coreTexture,
                    0f,
                    0f,
                    Width,
                    Height,
                    0f,
                    0f,
                    1f,
                    1f,
                    Tint);
            }
            PaintCaption(cx);
            return;
        }

        var (file, _) = EngagedMedia();
        if (MediaShown && file != 0)
        {
            var (bmp, tw, th) = _locate(file);
            if (bmp != 0 && tw != 0 && th != 0)
            {
                cx.SketchSprite(bmp, 0, 0, Width, Height, 0, 0, Width / tw, Height / th, Tint);
            }
        }

        PaintCaption(cx);
    }

    private void PaintCaption(WidgetRenderScope cx)
    {
        if (Label is { Length: > 0 } caption && LabelFont is { } font)
        {
            float tx = (Width - font.MeasureWidth(caption)) * 0.5f;
            float ty = (Height - font.LineHeight) * 0.5f;
            cx.PaintStringDat(font, caption, tx, ty, CaptionTint, Outline, OutlineColor);
        }
    }
}
