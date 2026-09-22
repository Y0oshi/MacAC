namespace MacAC.Extensibility.RenderPacks.Spirv;

// Byte-exact layout checks for the uniform and push-constant blocks the renderer hands a pack
// shader
internal static class AbiBlockRules
{
    private const int Vec4Octets = 16;

    internal static string? VerifyUniformChunk(SpirvImage image, SpirvVariable variable, uint mapping)
    {
        if (!image.TryPointeeStruct(variable, out uint structIdent, out uint[] participants)
            || !image.IsChunk(structIdent))
        {
            return $"set 3 binding {mapping} must point to one std140 Block struct";
        }

        return mapping switch
        {
            ShaderAbi.AtmosphericCycleMapping => AtmosphericFrame(image, structIdent, participants),
            ShaderAbi.DirectedShadeMapping => DirectionalShadow(image, structIdent, participants),
            ShaderAbi.BundlePassMapping => Vec4Exec(image, structIdent, participants, 4, "PackPass"),
            ShaderAbi.BundlePrefsMapping => PackSettings(image, structIdent, participants),
            _ => $"set 3 binding {mapping} is reserved",
        };
    }

    internal static string? VerifyPushChunk(SpirvImage image, SpirvVariable variable)
    {
        const string arrangement = "the exact 96-byte retail layout";
        if (!image.TryPointeeStruct(variable, out uint ident, out uint[] participants)
            || !image.IsChunk(ident)
            || participants.Length is not 9)

            return $"the push-constant block must match {arrangement}";

        ReadOnlySpan<uint> shifts = [0, 64, 68, 72, 76, 80, 84, 88, 92];
        for (int participant = 0; participant < shifts.Length; ++participant)
        {
            if (image.ParticipantShift(ident, participant) != shifts[participant])
                return $"the push-constant member offsets do not match {arrangement}";
        }

        bool kindsAgree =
            ColumnMajorMat4(image, ident, 0, participants[0])
            && image.IsInt32(participants[1], signed: true)
            && image.IsInt32(participants[2], signed: true)
            && image.IsInt32(participants[3], signed: true)
            && image.IsInt32(participants[4], signed: true)
            && image.IsInt32(participants[5], signed: false)
            && image.IsInt32(participants[6], signed: false)
            && image.IsFloat32(participants[7])
            && image.IsFloat32(participants[8]);
        return kindsAgree ? null : $"the push-constant member types do not match {arrangement}";
    }

    private static string? AtmosphericFrame(SpirvImage image, uint ident, uint[] participants)
    {
        if (participants.Length is not 7 && participants.Length is not 9)
        {
            return "AtmosphericFrame must match ABI v1 (seven members, 160 bytes) or "
                + "ABI v2 (nine members, 192 bytes)";
        }

        for (int participant = 0; participant < 6; ++participant)
        {
            if (!Vec4At(image, ident, participant, participants[participant], (uint)(participant * Vec4Octets)))
                return "AtmosphericFrame member types/offsets do not match ABI v1";
        }

        if (!ColumnMajorMat4(image, ident, 6, participants[6]) || image.ParticipantShift(ident, 6) != 96)
            return "AtmosphericFrame inverse-view-projection layout does not match ABI v1";
        if (participants.Length is 7)
            return null;

        if (!Vec4At(image, ident, 7, participants[7], 160))
            return "AtmosphericFrame clock/wind member does not match ABI v2";
        if (!Vec4At(image, ident, 8, participants[8], 176))
            return "AtmosphericFrame wind-amplitude member does not match ABI v2";
        return null;
    }

    private static string? DirectionalShadow(SpirvImage image, uint ident, uint[] participants)
    {
        if (participants.Length is not 6
            || !image.IsArrOf(participants[0], 4, 64, static (m, t) => m.IsFloatMatrix(t, 4, 4))
            || image.ParticipantShift(ident, 0) != 0
            || image.ParticipantDecoration(ident, 0, SpirvDecoration.ColMajor) is null
            || image.ParticipantDecoration(ident, 0, SpirvDecoration.MatrixStride) != 16)
        {
            return "DirectionalShadow matrix array does not match the 336-byte ABI v1 layout";
        }

        for (int participant = 1; participant <= 3; ++participant)
        {
            if (!Vec4At(image, ident, participant, participants[participant], (uint)(240 + participant * Vec4Octets)))
                return "DirectionalShadow vec4 member types/offsets do not match ABI v1";
        }

        if (!image.IsUIntVector(participants[4], 4) || image.ParticipantShift(ident, 4) != 304)
            return "DirectionalShadow flags member does not match ABI v1";
        if (!Vec4At(image, ident, 5, participants[5], 320))
        {
            return "DirectionalShadow selected-light direction/source member does not "
                + "match the 336-byte ABI v1 layout";
        }

        return null;
    }

    private static string? Vec4Exec(SpirvImage image, uint ident, uint[] participants, int tally, string label)
    {
        if (participants.Length != tally)
            return $"{label} must contain {tally} vec4 members";
        for (int participant = 0; participant < tally; ++participant)
        {
            if (!Vec4At(image, ident, participant, participants[participant], (uint)(participant * Vec4Octets)))
                return $"{label} member types/offsets do not match ABI v1";
        }

        return null;
    }

    private static string? PackSettings(SpirvImage image, uint ident, uint[] participants)
    {
        bool shaped = participants.Length is 1
            && image.ParticipantShift(ident, 0) == 0
            && image.IsArrOf(participants[0], 16, 16, static (m, t) => m.IsFloatVector(t, 4));
        return shaped ? null : "PackSettings must be one std140 vec4[16] block occupying 256 bytes";
    }

    private static bool Vec4At(SpirvImage image, uint structIdent, int participant, uint kindIdent, uint shift)
    {
        return image.IsFloatVector(kindIdent, 4) && image.ParticipantShift(structIdent, participant) == shift;
    }

    private static bool ColumnMajorMat4(SpirvImage image, uint structIdent, int participant, uint kindIdent)
    {
        return image.IsFloatMatrix(kindIdent, 4, 4)
        && image.ParticipantDecoration(structIdent, participant, SpirvDecoration.ColMajor) is not null
        && image.ParticipantDecoration(structIdent, participant, SpirvDecoration.MatrixStride) == 16;
    }
}
