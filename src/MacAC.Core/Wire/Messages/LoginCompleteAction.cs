namespace MacAC.Wire.Messages;

/// <summary>GameAction(LoginComplete): envelope + zero sequence + action type, 12 bytes.</summary>
public static class LoginCompleteAction
{
    public const uint PlayActionOpcode = GameActionScribe.Envelope;
    public const uint SigninDoneActKind = 0x000000A1u;

    public static byte[] Build() => new GameActionScribe(0u, SigninDoneActKind, 16).Bytes();
}
