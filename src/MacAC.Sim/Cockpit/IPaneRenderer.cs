using System.Numerics;

namespace MacAC.Cockpit;

public interface IPaneRenderer
{
    bool Begin(string banner);

    void End();

    /// <summary>One plain line; no markup.</summary>
    void Text(string phrase);

    /// <summary>Puts the next widget beside the previous one.</summary>
    void SameLine();

    void Separator();

    void ProgressBar(float ratio, float width, string? topLayer = null);

    void TextColored(Vector4 rgba, string phrase);

    bool CollapsingHeader(string caption, bool defaultOpen = true);

    bool TreeNode(string caption);

    void TreePop();

    bool Checkbox(string caption, ref bool val);

    bool Button(string caption);

    bool Combo(string caption, ref int chosenOrdinal, string[] gearList);

    bool SliderFloat(string caption, ref float val, float lower, float upper);

    void PlotLines(string caption, float[] vals, int tally, int shift = 0, string? topLayer = null, float? lower = null, float? upper = null, Vector2? dims = null);

    void BeginTable(string ident, int columns);

    void TableNextColumn();

    void EndTable();

    bool InputTextSubmit(string caption, ref string buf, int upperLength, out string? submitted);

    /// <summary>A small vertical gap.</summary>
    void Spacing();

    /// <summary>Invisible space, for layout padding.</summary>
    void Dummy(Vector2 dims);

    void TextWrapped(string phrase);

    bool BeginChild(string ident, Vector2 dims, bool border = false);

    void EndChild();

    float FrameHeightWithSpacing();

    void SetScrollHereY(float ratio);

    void SetKeyboardFocusHere();

    bool BeginMainMenuBar();

    void EndMainMenuBar();

    bool BeginMenu(string caption);

    void EndMenu();

    bool MenuGear(string caption, string? shortcut = null);

    bool BeginTabBar(string ident);

    void EndTabBar();

    bool BeginTabItem(string caption);

    void EndTabItem();

    void TextMultilineReadOnly(string ident, string substance, Vector2 dims);
}
