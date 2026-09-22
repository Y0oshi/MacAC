using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public enum ClientHJustify : byte { Left = 0, Center = 1, Right = 2 }

public enum ClientVJustify : byte { Top = 0, Center = 1, Bottom = 2 }

public readonly record struct WidgetTabChartEntry(uint ButtonElementId, uint PageElementId, bool IsDefault);

public readonly record struct WidgetTemplateListEntry(uint TemplateLayoutId, uint TemplateElementId);

public sealed class ElemDetails
{
    public uint Id;

    public uint Type;

    public float X, Y, Width, Height;

    public float OriginalParentWidth, OriginalParentHeight;
    public bool HasOriginalParentSize;

    public uint Left, Top, Right, Bottom;

    public uint ReadOrder;

    public uint ZLevel;

    public Dictionary<uint, WidgetStateInfo> States = [];

    public uint DefaultStateId;

    public uint FontDid;

    public ClientHJustify HJustify = ClientHJustify.Left;

    public ClientVJustify VJustify = ClientVJustify.Top;

    public Vector4? FontColor;

    public Vector4? TagFontColor;

    public bool Outline;

    public Vector4? OutlineColor;

    public Dictionary<string, (uint File, int DrawMode)> StateMedia = [];

    public Dictionary<string, WidgetCursorMedia> StateCursors = [];

    public string DefaultStateName = "";

    public List<ElemDetails> Children = [];

    public List<WidgetTabChartEntry> TabTable = [];

    public List<WidgetTemplateListEntry> TemplateList = [];

    public uint LedCheckedSprite;

    public uint LedUncheckedSprite;

    public uint ScrollbarElementId;

    public bool Invisible;

    public int MarginLeft, MarginRight, MarginTop, MarginBottom;

    public bool TooltipEnabled;

    public WidgetStringInfoValue? TooltipText;

    public uint TooltipRootElementId;

    public uint TooltipLayoutDid;

    public uint TooltipTextChildElementId;

    public float? TooltipDelaySeconds;

    public int? MaxWidth;
    public int? MinWidth;

    public int? MaxHeight;
    public int? MinHeight;

    public bool TryFetchNetProp(uint propIdent, out WidgetPropertyValue val, uint? phaseIdent = null)
    {
        uint netPhase = phaseIdent ?? NetDefaultPhaseIdent();

        WidgetPropertyValue? settled = null;
        if (States.TryGetValue(WidgetStateInfo.StraightPhaseIdent, out var straight)
            && straight.Properties.TryGetValue(propIdent, out var straightVal))

            settled = straightVal;

        if (netPhase != WidgetStateInfo.StraightPhaseIdent
            && States.TryGetValue(netPhase, out var phase)
            && phase.Properties.TryGetValue(propIdent, out var phaseVal))

            settled = phaseVal;

        val = settled!;
        return settled is not null;
    }

    public bool TryFetchNetBool(uint propIdent, out bool val, uint? phaseIdent = null)
    {
        if (TryFetchNetProp(propIdent, out var prop, phaseIdent)
            && prop.Kind == WidgetPropertyKind.Bool)
        {
            val = prop.BoolValue;
            return true;
        }

        val = false;
        return false;
    }

    public bool TryFetchNetInteger(uint propIdent, out int val, uint? phaseIdent = null)
    {
        if (TryFetchNetProp(propIdent, out var prop, phaseIdent)
            && prop.Kind == WidgetPropertyKind.Integer)
        {
            val = prop.IntegerValue;
            return true;
        }

        val = 0;
        return false;
    }

    public bool TryFetchNetFloat(uint propIdent, out float val, uint? phaseIdent = null)
    {
        if (TryFetchNetProp(propIdent, out var prop, phaseIdent)
            && prop.Kind == WidgetPropertyKind.Float)
        {
            val = prop.FloatValue;
            return true;
        }

        val = 0f;
        return false;
    }

    public uint NetDefaultPhaseIdent()
    {
        if (DefaultStateId is not 0 && States.ContainsKey(DefaultStateId))
            return DefaultStateId;
        return States.ContainsKey(1u) ? 1u : WidgetStateInfo.StraightPhaseIdent;
    }
}

public static partial class ElemScanner
{

}
