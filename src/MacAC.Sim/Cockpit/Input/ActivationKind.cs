namespace MacAC.Cockpit.Input;

/// <summary>Which edge or gesture of a chord triggers its action.</summary>
public enum ActivationKind
{
    /// <summary>Key-down; the usual case.</summary>
    Press,

    /// <summary>Key-up; paired walk-mode toggles use it.</summary>
    Release,
    Hold,
    DoubleClick,
    Click,

    /// <summary>Mouse axis or other analog input - reserved, never emitted yet.</summary>
    Analog,
}
