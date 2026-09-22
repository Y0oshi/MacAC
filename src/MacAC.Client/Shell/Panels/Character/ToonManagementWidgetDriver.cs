using System.Numerics;
using System.Reflection;
using MacAC.Client.Graphics;
using MacAC.Sim;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonManagementWidgetDriver : IDisposable
{
    internal const uint TrunkEnum = 0x10000005u;

    internal const uint TrunkElemIdent = 0x1000039Au;

    internal const uint RealmPhraseElemIdent = 0x1000039Bu;

    internal const uint RosterElemIdent = 0x1000039Du;

    internal const uint BuildElemIdent = 0x100003A0u;

    internal const uint JoinElemIdent = 0x100003A2u;

    internal const uint EraseElemIdent = 0x1000039Fu;

    internal const uint RevertElemIdent = 0x1000039Eu;

    internal const uint CreditsElemIdent = 0x100003A3u;

    internal const uint QuitElemIdent = 0x100003A4u;

    internal sealed record PromptStrings(
        Func<string, string> DeleteConfirmation,
        string DeleteResponse,
        string PleaseWait,
        string EnteringWorld,
        string ConfirmExit);

    private readonly WidgetTrunk _hub;

    private readonly ImportedArrangement _arrangement;

    private readonly WidgetPhrase _realmPhrase;

    private readonly WidgetBlueprintRosterBbox _roster;

    private readonly WidgetBtn _build;

    private readonly WidgetBtn _join;

    private readonly WidgetBtn _erase;

    private readonly WidgetBtn _revert;

    private readonly WidgetBtn _credits;

    private readonly WidgetBtn _quit;

    private readonly CanonPromptMint _popups;

    private readonly ToonPickingEngineWiring _bindings;

    private readonly PromptStrings _texts;

    private readonly List<WidgetBtn> _ranks = [];

    private readonly Dictionary<WidgetBtn, uint> _rankIdents = [];

    private Vector2 _authoredCanvas;

    private SimEpochTicket _previousGen;

    private long _previousRev = long.MinValue;

    private string _previousRealmLabel = string.Empty;

    private uint _erasePopupCtx;

    private uint _opPauseCtx;

    private uint _joinPauseCtx;

    private uint _problemPopupCtx;

    private uint _confirmQuitPopupCtx;

    private bool _engaged;

    private bool _exhibitSuppressed;

    private bool _straightLaunchQueued;

    private bool _revertDirectiveInFlight;

    private bool _suppressPopupHooks;

    private bool _destroyed;

    private ToonManagementWidgetDriver(
        WidgetTrunk hub,
        ImportedArrangement arrangement,
        WidgetPhrase realmPhrase,
        WidgetBlueprintRosterBbox roster,
        WidgetBtn build,
        WidgetBtn enter,
        WidgetBtn erase,
        WidgetBtn revert,
        WidgetBtn credits,
        WidgetBtn quit,
        CanonPromptMint popups,
        ToonPickingEngineWiring mappings,
        PromptStrings texts,
        Action? openCredits,
        BitmapFont? verTypeface)
    {
        _hub = hub;
        _arrangement = arrangement;
        _realmPhrase = realmPhrase;
        _roster = roster;
        _build = build;
        _join = enter;
        _erase = erase;
        _revert = revert;
        _credits = credits;
        _quit = quit;
        _popups = popups;
        _bindings = mappings;
        _straightLaunchQueued = mappings.DirectCharacterLaunch;
        _texts = texts;

        Root.Left = 0f;
        Root.Top = 0f;
        Root.ClickThrough = false;
        Root.Visible = false;

        _authoredCanvas = new Vector2(
            Root.Width > 0f ? Root.Width : 800f,
            Root.Height > 0f ? Root.Height : 600f);

        _build.Visible = true;
        _build.Enabled = false;
        _build.OnClick = ReqBuild;
        _join.OnClick = JoinChosen;
        _erase.OnClick = ReqErase;
        _revert.OnClick = ReinstateChosen;

        _credits.Visible = true;
        _credits.Enabled = openCredits is not null;
        _credits.OnClick = openCredits;
        _quit.OnClick = ReqQuit;

        _realmPhrase.StrokesSupplier =
            () => [new WidgetPhrase.Line(_previousRealmLabel, _realmPhrase.DefaultTint)];

        Assembly assembly = typeof(ToonManagementWidgetDriver).Assembly;
        string ver = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
        WidgetPhrase.Line[] verStrokes =
            [new($"MacAC {ver}", new Vector4(0.65f, 0.65f, 0.65f, 1f))];
        Root.AddChild(new WidgetPhrase
        {
            Name = "ClientVersion",
            Left = 8f,
            Top = 6f,
            Width = _authoredCanvas.X - 16f,
            Height = 24f,
            ZOrder = int.MaxValue,
            OneLine = true,
            ClickThrough = true,
            Font = verTypeface,
            Outline = true,
            StrokesSupplier = () => verStrokes,
        });
    }
}
