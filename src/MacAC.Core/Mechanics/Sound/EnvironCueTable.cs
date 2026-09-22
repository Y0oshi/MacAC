using System.Collections.Frozen;

namespace MacAC.Mechanics.Sound;

/// <summary>EnvironChange codes that play a UI sound rather than change the sky.</summary>
public static class EnvironCueTable
{
    private static readonly FrozenDictionary<uint, SfxId> Cues = new Dictionary<uint, SfxId>
    {
        [0x65u] = SfxId.UI_Roar,
        [0x66u] = SfxId.UI_Bell,
        [0x67u] = SfxId.UI_Chant1,
        [0x68u] = SfxId.UI_Chant2,
        [0x69u] = SfxId.UI_DarkWhispers1,
        [0x6Au] = SfxId.UI_DarkWhispers2,
        [0x6Bu] = SfxId.UI_DarkLaugh,
        [0x6Cu] = SfxId.UI_DarkWind,
        [0x6Du] = SfxId.UI_DarkSpeech,
        [0x6Eu] = SfxId.UI_Drums,
        [0x6Fu] = SfxId.UI_GhostSpeak,
        [0x70u] = SfxId.UI_Breathing,
        [0x71u] = SfxId.UI_Howl,
        [0x72u] = SfxId.UI_LostSouls,
        [0x75u] = SfxId.UI_Squeal,
        [0x76u] = SfxId.UI_Thunder1,
        [0x77u] = SfxId.UI_Thunder2,
        [0x78u] = SfxId.UI_Thunder3,
        [0x79u] = SfxId.UI_Thunder4,
        [0x7Au] = SfxId.UI_Thunder5,
        [0x7Bu] = SfxId.UI_Thunder6,
    }.ToFrozenDictionary();

    public static bool TryFetchSfx(uint editKind, out SfxId sfx) => Cues.TryGetValue(editKind, out sfx);

    public static IReadOnlyCollection<uint> Codes => Cues.Keys;
}
