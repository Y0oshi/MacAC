using System.Numerics;

namespace MacAC.Cockpit.Panels.Chat;

public static class CanonChatColorTable
{
    private static class Paint
    {
        public static readonly Vector4 White = new(1f, 1f, 1f, 1f);
        public static readonly Vector4 Yellow = new(1f, 1f, 0.247f, 1f);
        public static readonly Vector4 DarkYellow = new(0.824f, 0.824f, 0.392f, 1f);
        public static readonly Vector4 BrightPurple = new(1f, 0.498f, 1f, 1f);
        public static readonly Vector4 DarkRed = new(1f, 0.247f, 0.247f, 1f);
        public static readonly Vector4 LampRed = new(0.96f, 0.459f, 0.447f, 1f);
        public static readonly Vector4 LampBlue = new(0.247f, 0.749f, 1f, 1f);
        public static readonly Vector4 Pink = new(1f, 0.588f, 0.588f, 1f);
        public static readonly Vector4 Cyan = new(0.247f, 0.863f, 0.863f, 1f);
        public static readonly Vector4 BlueGrey = new(0.706f, 0.863f, 0.941f, 1f);
        public static readonly Vector4 Grey = new(0.824f, 0.824f, 0.784f, 1f);
        public static readonly Vector4 Orange = new(0.933f, 0.573f, 0.118f, 1f);
        public static readonly Vector4 Green = new(0.5f, 1f, 0.498f, 1f);
        public static readonly Vector4 BrightRed = new(1f, 0f, 0f, 1f);
    }

    public static readonly IReadOnlyList<Vector4> Tints =
    [
        Paint.Green,        // 0x00 Default
        Paint.Green,        // 0x01 All
        Paint.White,        // 0x02 Speech
        Paint.Yellow,       // 0x03 Tell
        Paint.DarkYellow,   // 0x04 Speech_Direct_Send
        Paint.BrightPurple, // 0x05 System
        Paint.DarkRed,      // 0x06
        Paint.LampBlue,    // 0x07 Magic
        Paint.Pink,         // 0x08
        Paint.Pink,         // 0x09
        Paint.Yellow,       // 0x0A Social
        Paint.DarkYellow,   // 0x0B Social_Send
        Paint.Grey,         // 0x0C Emote
        Paint.Cyan,         // 0x0D Advancement
        Paint.BlueGrey,     // 0x0E Abuse
        Paint.DarkRed,      // 0x0F Help
        Paint.Green,        // 0x10 Appraisal
        Paint.LampBlue,    // 0x11 Spellcasting
        Paint.Orange,       // 0x12 Allegiance
        Paint.Yellow,       // 0x13 Fellowship
        Paint.Green,        // 0x14 World_Broadcast
        Paint.DarkRed,      // 0x15
        Paint.LampRed,     // 0x16
        Paint.Green,        // 0x17 Recall
        Paint.Green,        // 0x18
        Paint.Green,        // 0x19 Salvaging
        Paint.BrightRed,    // 0x1A
        Paint.BlueGrey,     // 0x1B
        Paint.BlueGrey,     // 0x1C
        Paint.BlueGrey,     // 0x1D
        Paint.BlueGrey,     // 0x1E
        Paint.Yellow,       // 0x1F Admin_Tell
        Paint.BlueGrey,     // 0x20
        Paint.Orange,       // 0x21 (reserved)
    ];

    public static bool TryFetchColor(uint tracePhraseKind, out Vector4 tint)
    {
        bool recognized = tracePhraseKind < (uint)Tints.Count;
        tint = recognized ? Tints[(int)tracePhraseKind] : default;
        return recognized;
    }
}
