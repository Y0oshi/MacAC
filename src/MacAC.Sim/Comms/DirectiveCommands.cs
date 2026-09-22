namespace MacAC.Sim.Comms;

/// <summary>The commands the chat router publishes on the directive bus.</summary>
public sealed record SendCommsCmd(CommsChannelKind Channel, string? TargetName, string Text);

public sealed record TransmitCrudeLaneCmd(uint ChannelId, string Text);

public sealed record SendServerDirectiveCmd(string Text);

public sealed record ExecuteClientDirectiveCmd(ClientDirectiveId Command, string Arguments);
