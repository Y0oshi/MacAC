using System.Numerics;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonCreationSkillsPage : IDisposable
{
    private enum AptitudeBin
    {
        Specialized,
        Trained,
        UseableUntrained,
        UnuseableUntrained,
    }

    private static readonly (AptitudeBin Bucket, string StringKey)[] BinOrdering =
    [
        (AptitudeBin.Specialized, "ID_CharGen_Specialized"),
        (AptitudeBin.Trained, "ID_CharGen_Trained"),
        (AptitudeBin.UseableUntrained, "ID_CharGen_UseableUntrained"),
        (AptitudeBin.UnuseableUntrained, "ID_CharGen_UnuseableUntrained"),
    ];

    private const uint PreambleLegendElemIdent = 0x100002F6u;

    private const uint RankLabelPhraseIdent = 0x10000301u;

    private const uint RankTierPhraseIdent = 0x10000302u;

    private const uint RankUpPricePhraseIdent = 0x10000303u;

    private const uint RankDownPricePhraseIdent = 0x10000306u;

    private const uint RankUpBtnIdent = 0x10000304u;

    private const uint RankDownBtnIdent = 0x10000305u;

    private const uint ArrowGhostedPhaseIdent = 0x1000001Au;

    private const uint ArrowTurnedOnPhaseIdent = 0x1000001Bu;

    private static readonly Vector4 ChosenLabelTint = Vector4.One;

    private readonly record struct AptitudeRank(
        WidgetElem Root,
        uint SkillId,
        AptitudeBin Bucket,
        WidgetPhrase? NameText,
        WidgetPhrase? LevelText,
        WidgetPhrase? UpCostText,
        WidgetPhrase? DownCostText,
        WidgetBtn? UpButton,
        WidgetBtn? DownButton,
        Vector4 UnselectedNameColor);

    private readonly ToonCreationEngineWiring _bindings;

    private readonly WidgetBlueprintRosterBbox? _roster;

    private readonly WidgetBtn? _credits;

    private readonly WidgetPhrase? _detailsBanner;

    private readonly WidgetPhrase? _detailsPhrase;

    private readonly List<AptitudeRank> _ranks = [];

    private uint _previousLineageIdent;

    private uint? _chosenAptitudeIdent;

    private bool _ranksBuilt;

    private bool _destroyed;

    private const uint DetailsBboxCycleElemIdent = 0x100003FAu;
}
