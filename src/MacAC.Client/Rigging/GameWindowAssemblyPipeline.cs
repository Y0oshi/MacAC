namespace MacAC.Client.Rigging;

internal interface IHostInputCameraAssemblyPhase<in TPlatform, out TResult>
{
    TResult Compose(TPlatform platform);
}

internal interface IContentEffectsAudioAssemblyPhase<in TPlatform, in THost, out TResult>
{
    TResult Compose(TPlatform platform, THost hub);
}

internal interface IPreferencesDevToolsAssemblyPhase<in TPlatform, in THost, in TContent, out TResult>
{
    TResult Compose(TPlatform platform, THost hub, TContent substance);
}

internal interface IRealmRenderAssemblyPhase<in TPlatform, in TContent, in TSettings, out TResult>
{
    TResult Compose(TPlatform platform, TContent substance, TSettings prefs);
}

internal interface IDealingWidgetAssemblyPhase<in TPlatform, in THost, in TContent, in TSettings, in TWorld, out TResult>
{
    TResult Compose(TPlatform platform, THost hub, TContent substance, TSettings prefs, TWorld realm);
}

internal interface IOnlineDisplayAssemblyPhase<in TPlatform, in THost, in TContent, in TSettings, in TWorld, in TInteraction, out TResult>
{
    TResult Compose(
        TPlatform platform,
        THost hub,
        TContent substance,
        TSettings prefs,
        TWorld realm,
        TInteraction dealing);
}

internal interface ISessionAvatarAssemblyPhase<in THost, in TContent, in TSettings, in TWorld, in TInteraction, in TLive, out TResult>
{
    TResult Compose(
        THost hub,
        TContent substance,
        TSettings prefs,
        TWorld realm,
        TInteraction dealing,
        TLive online);
}

internal interface IFrameRootAssemblyPhase<in TPlatform, in THost, in TContent, in TSettings, in TWorld, in TInteraction, in TLive, in TSession, out TResult>
{
    TResult Compose(
        TPlatform platform,
        THost hub,
        TContent substance,
        TSettings prefs,
        TWorld realm,
        TInteraction dealing,
        TLive online,
        TSession sess);
}

internal interface ISessionStartAssemblyPhase<in TFrame>
{
    void Start(TFrame cycle);
}

internal static class GameWindowAssemblyPipeline
{
    public static void Run<
        TPlatform,
        THost,
        TContent,
        TSettings,
        TWorld,
        TInteraction,
        TLive,
        TSession,
        TFrame>(
        TPlatform platform,
        Func<TPlatform, THost> hub,
        Func<TPlatform, THost, TContent> substance,
        Func<TPlatform, THost, TContent, TSettings> prefs,
        Func<TPlatform, TContent, TSettings, TWorld> realm,
        Func<TPlatform, THost, TContent, TSettings, TWorld, TInteraction> dealing,
        Func<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive> online,
        Func<THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession> sess,
        Func<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession, TFrame> cycle,
        Action<TFrame> begin)
    {
        new GameWindowAssemblyPipeline<
            TPlatform,
            THost,
            TContent,
            TSettings,
            TWorld,
            TInteraction,
            TLive,
            TSession,
            TFrame>(
                new HubStage<TPlatform, THost>(hub),
                new SubstanceStage<TPlatform, THost, TContent>(substance),
                new PreferencesPhase<TPlatform, THost, TContent, TSettings>(prefs),
                new RealmPhase<TPlatform, TContent, TSettings, TWorld>(realm),
                new DealingPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction>(dealing),
                new OnlinePhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive>(online),
                new SessionStage<THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession>(sess),
                new CycleStage<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession, TFrame>(cycle),
                new BeginStage<TFrame>(begin))
            .Run(platform);
    }

    private sealed class HubStage<TPlatform, TResult>(
        Func<TPlatform, TResult> construct)
        : IHostInputCameraAssemblyPhase<TPlatform, TResult>
    {
        public TResult Compose(TPlatform platform) => construct(platform);
    }

    private sealed class SubstanceStage<TPlatform, THost, TResult>(
        Func<TPlatform, THost, TResult> construct)
        : IContentEffectsAudioAssemblyPhase<TPlatform, THost, TResult>
    {
        public TResult Compose(TPlatform platform, THost hub) => construct(platform, hub);
    }

    private sealed class PreferencesPhase<TPlatform, THost, TContent, TResult>(
        Func<TPlatform, THost, TContent, TResult> construct)
        : IPreferencesDevToolsAssemblyPhase<TPlatform, THost, TContent, TResult>
    {
        public TResult Compose(TPlatform platform, THost hub, TContent substance) =>
            construct(platform, hub, substance);
    }

    private sealed class RealmPhase<TPlatform, TContent, TSettings, TResult>(
        Func<TPlatform, TContent, TSettings, TResult> construct)
        : IRealmRenderAssemblyPhase<TPlatform, TContent, TSettings, TResult>
    {
        public TResult Compose(TPlatform platform, TContent substance, TSettings prefs) =>
            construct(platform, substance, prefs);
    }

    private sealed class DealingPhase<TPlatform, THost, TContent, TSettings, TWorld, TResult>(
        Func<TPlatform, THost, TContent, TSettings, TWorld, TResult> construct)
        : IDealingWidgetAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TResult>
    {
        public TResult Compose(
            TPlatform platform,
            THost hub,
            TContent substance,
            TSettings prefs,
            TWorld realm) => construct(platform, hub, substance, prefs, realm);
    }

    private sealed class OnlinePhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TResult>(
        Func<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TResult> construct)
        : IOnlineDisplayAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TResult>
    {
        public TResult Compose(
            TPlatform platform,
            THost hub,
            TContent substance,
            TSettings prefs,
            TWorld realm,
            TInteraction dealing) =>
            construct(platform, hub, substance, prefs, realm, dealing);
    }

    private sealed class SessionStage<THost, TContent, TSettings, TWorld, TInteraction, TLive, TResult>(
        Func<THost, TContent, TSettings, TWorld, TInteraction, TLive, TResult> construct)
        : ISessionAvatarAssemblyPhase<THost, TContent, TSettings, TWorld, TInteraction, TLive, TResult>
    {
        public TResult Compose(
            THost hub,
            TContent substance,
            TSettings prefs,
            TWorld realm,
            TInteraction dealing,
            TLive online) => construct(hub, substance, prefs, realm, dealing, online);
    }

    private sealed class CycleStage<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession, TResult>(
        Func<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession, TResult> construct)
        : IFrameRootAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession, TResult>
    {
        public TResult Compose(
            TPlatform platform,
            THost hub,
            TContent substance,
            TSettings prefs,
            TWorld realm,
            TInteraction dealing,
            TLive online,
            TSession sess)
        {
            return construct(platform, hub, substance, prefs, realm, dealing, online, sess);
        }
    }

    private sealed class BeginStage<TFrame>(Action<TFrame> begin)
        : ISessionStartAssemblyPhase<TFrame>
    {
        public void Start(TFrame cycle) => begin(cycle);
    }
}

internal sealed class GameWindowAssemblyPipeline<
    TPlatform,
    THost,
    TContent,
    TSettings,
    TWorld,
    TInteraction,
    TLive,
    TSession,
    TFrame>(
    IHostInputCameraAssemblyPhase<TPlatform, THost> host,
    IContentEffectsAudioAssemblyPhase<TPlatform, THost, TContent> content,
    IPreferencesDevToolsAssemblyPhase<TPlatform, THost, TContent, TSettings> settings,
    IRealmRenderAssemblyPhase<TPlatform, TContent, TSettings, TWorld> world,
    IDealingWidgetAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction> interaction,
    IOnlineDisplayAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive> live,
    ISessionAvatarAssemblyPhase<THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession> session,
    IFrameRootAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession, TFrame> frame,
    ISessionStartAssemblyPhase<TFrame> start)
{
    private readonly IHostInputCameraAssemblyPhase<TPlatform, THost> _hub = host ?? throw new ArgumentNullException(nameof(host));
    private readonly IContentEffectsAudioAssemblyPhase<TPlatform, THost, TContent> _substance = content ?? throw new ArgumentNullException(nameof(content));
    private readonly IPreferencesDevToolsAssemblyPhase<TPlatform, THost, TContent, TSettings> _prefs = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IRealmRenderAssemblyPhase<TPlatform, TContent, TSettings, TWorld> _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly IDealingWidgetAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction> _dealing = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IOnlineDisplayAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive> _online = live ?? throw new ArgumentNullException(nameof(live));
    private readonly ISessionAvatarAssemblyPhase<THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession> _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly IFrameRootAssemblyPhase<TPlatform, THost, TContent, TSettings, TWorld, TInteraction, TLive, TSession, TFrame> _cycle = frame ?? throw new ArgumentNullException(nameof(frame));
    private readonly ISessionStartAssemblyPhase<TFrame> _begin = start ?? throw new ArgumentNullException(nameof(start));

    public void Run(TPlatform platform)
    {
        THost hub = _hub.Compose(platform);
        TContent substance = _substance.Compose(platform, hub);
        TSettings prefs = _prefs.Compose(platform, hub, substance);
        TWorld realm = _world.Compose(platform, substance, prefs);
        TInteraction dealing = _dealing.Compose(platform, hub, substance, prefs, realm);
        TLive online = _online.Compose(platform, hub, substance, prefs, realm, dealing);
        TSession sess = _session.Compose(hub, substance, prefs, realm, dealing, online);
        TFrame cycle = _cycle.Compose(
            platform,
            hub,
            substance,
            prefs,
            realm,
            dealing,
            online,
            sess);
        _begin.Start(cycle);
    }
}
