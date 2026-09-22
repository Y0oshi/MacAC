using MacAC.Wire.Packets;

namespace MacAC.Wire.Messages;

public static class CharacterForge
{
    public const uint Opcode = 0xF656u;

    /// <summary>ACE drops the session on any other count.</summary>
    public const int AptitudeAdvancementClassTally = 55;

    public readonly record struct Looks(
        uint EyesStrip,
        uint NoseStrip,
        uint MouthStrip,
        uint HairColor,
        uint EyeColor,
        uint HairStyle,
        uint HeadgearStyle,
        uint HeadgearColor,
        uint ShirtStyle,
        uint ShirtColor,
        uint TrousersStyle,
        uint TrousersColor,
        uint FootwearStyle,
        uint FootwearColor,
        double SkinShade,
        double HairShade,
        double HeadgearShade,
        double ShirtShade,
        double TrousersShade,
        double FootwearShade);

    public readonly record struct Attrs(uint Strength, uint Endurance, uint Coordination, uint Quickness, uint Focus, uint Self);

    public readonly record struct WireRequest(
        uint Heritage,
        uint Gender,
        Looks Appearance,
        uint Template,
        Attrs Attributes,
        uint Slot,
        uint ClassId,
        string Name,
        uint StartArea,
        bool IsAdmin,
        bool IsEnvoy);

    public static byte[] AssembleRequestBody(string acctLabel, WireRequest req, ReadOnlySpan<uint> skillAdvancementClasses)
    {
        ArgumentNullException.ThrowIfNull(acctLabel);
        ArgumentNullException.ThrowIfNull(req.Name);
        if (skillAdvancementClasses.Length != AptitudeAdvancementClassTally)
        {
            throw new ArgumentException(
                "retail's CG_Pack numSkills has to be precisely "
                + $"{AptitudeAdvancementClassTally} - ACE terminates the session "
                + "(PlayerFactory.CreateResult.ClientServerSkillsMismatch) on "
                + $"any other count. Got {skillAdvancementClasses.Length}.",
                nameof(skillAdvancementClasses));
        }

        Looks gaze = req.Appearance;
        Attrs stats = req.Attributes;
        DatagramScribe scribe = new DatagramScribe(256 + skillAdvancementClasses.Length * 4 + req.Name.Length * 2);
        scribe.EmitUInt32(Opcode);
        scribe.EmitString16L(acctLabel);
        scribe.EmitUInt32(1u); // CG_Pack version
        scribe.EmitUInt32(req.Heritage);
        scribe.EmitUInt32(req.Gender);
        foreach (uint choice in (ReadOnlySpan<uint>)
        [
            gaze.EyesStrip, gaze.NoseStrip, gaze.MouthStrip, gaze.HairColor, gaze.EyeColor, gaze.HairStyle,
            gaze.HeadgearStyle, gaze.HeadgearColor, gaze.ShirtStyle, gaze.ShirtColor,
            gaze.TrousersStyle, gaze.TrousersColor, gaze.FootwearStyle, gaze.FootwearColor,
        ])
        {
            scribe.EmitUInt32(choice);
        }
        foreach (double shade in (ReadOnlySpan<double>)[gaze.SkinShade, gaze.HairShade, gaze.HeadgearShade, gaze.ShirtShade, gaze.TrousersShade, gaze.FootwearShade])
            scribe.EmitDouble(shade);
        scribe.EmitUInt32(req.Template);
        foreach (uint stat in (ReadOnlySpan<uint>)[stats.Strength, stats.Endurance, stats.Coordination, stats.Quickness, stats.Focus, stats.Self])
            scribe.EmitUInt32(stat);
        scribe.EmitUInt32(req.Slot);
        scribe.EmitUInt32(req.ClassId);
        scribe.EmitUInt32((uint)skillAdvancementClasses.Length);
        foreach (uint aptitude in skillAdvancementClasses)
            scribe.EmitUInt32(aptitude);
        scribe.EmitString16L(req.Name);
        scribe.EmitUInt32(req.StartArea);
        scribe.EmitUInt32(req.IsAdmin ? 1u : 0u);
        scribe.EmitUInt32(req.IsEnvoy ? 1u : 0u);
        scribe.EmitUInt32(CalculateChecksum(req));
        return scribe.ToArray();
    }

    public static uint CalculateChecksum(WireRequest req)
    {
        Looks appearance = req.Appearance;
        Attrs attributes = req.Attributes;
        return unchecked(
            req.Heritage + req.Gender
            + appearance.EyesStrip + appearance.NoseStrip + appearance.MouthStrip + appearance.HairColor + appearance.EyeColor + appearance.HairStyle
            + appearance.HeadgearStyle + appearance.ShirtStyle + appearance.TrousersStyle + appearance.FootwearStyle
            + req.Template
            + attributes.Strength + attributes.Endurance + attributes.Coordination + attributes.Quickness + attributes.Focus + attributes.Self);
    }
}
