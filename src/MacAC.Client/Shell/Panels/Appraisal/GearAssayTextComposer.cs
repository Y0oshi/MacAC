namespace MacAC.Client.Shell.Panels;

public static partial class GearAssayTextComposer
{
    private sealed class CanonDigestAssembler
    {
        private readonly GearAssayDigestAssembler _dossier = new();

        public void Line(
            string val,
            GearAssayFontStyle styling = GearAssayFontStyle.Normal)
            => _dossier.Line(val, styling);

        public void Paragraph(
            string val,
            GearAssayFontStyle styling = GearAssayFontStyle.Normal)
            => _dossier.Paragraph(val, styling);

        public void BlankLine(
            GearAssayFontStyle styling = GearAssayFontStyle.Normal)
            => _dossier.BlankStroke(styling);

        public GearAssayDigest Build() => _dossier.Build();
    }
}
