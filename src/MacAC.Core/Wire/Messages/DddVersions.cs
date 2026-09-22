namespace MacAC.Wire.Messages;

/// <summary>Installed DAT iteration counts reported during the data check.</summary>
public readonly record struct DddVersions(int Portal, int Cell, int Language);
