namespace MacAC.Cockpit.Input;

public enum InputLayer
{
    /// <summary>Esc, F1, F11 and friends: they fire whatever has focus.</summary>
    Always,
    Game,
    Chat,
    EditField,
    Dialog,
    MeleeCombat,
    MissileCombat,
    MagicCombat,
    Camera,
}
