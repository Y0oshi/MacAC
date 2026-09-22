namespace MacAC.Client.Graphics;

internal static partial class PlayPaneShutdownManifest
{
    public static AssetShutdownTransaction Create(PlayPaneShutdownTrunks trunks)
    {
        ArgumentNullException.ThrowIfNull(trunks);
        var ingress = trunks.Ingress;
        var cycle = trunks.Frame;
        var online = trunks.Live;
        var rasterize = trunks.Render;
        var platform = trunks.Platform;

        return new AssetShutdownTransaction(
            new AssetShutdownJuncture("host and session barriers",
            [
                Hard("host quiescence", ingress.HostQuiescence.HaltAccepting),
                Hard("combat command slot", ingress.CombatCommands.Deactivate),
                Hard("diagnostic command slot", ingress.DiagnosticCommands.Deactivate),
                Hard("retained gameplay", () => ingress.RetainedGameplay?.Deactivate()),
                Hard("gameplay actions", () => ingress.GameplayActions?.Disarm()),
                Hard("camera pointer", () => ingress.CameraPointer?.Disengage()),
                Hard("dispatcher", () => ingress.Dispatcher?.Deactivate()),
                Hard("mouse source", () => ingress.MouseSource?.Deactivate()),
                Hard("keyboard source", () => ingress.KeyboardSource?.Deactivate()),
                Hard("retained UI input", ingress.RetailUi.QuiesceFeed),
                Hard("game runtime session", ingress.Runtime.HaltSess),
            ]),
            new AssetShutdownJuncture("physical ingress cleanup",
            [
                Soft("retained gameplay", () => TeardownKeptGameplay(ingress.RetainedGameplay)),
                Soft("gameplay actions", () => TeardownGameplayActs(ingress.GameplayActions)),
                Soft("retained UI input", ingress.RetailUi.DisengageFeed),
                Soft("camera pointer", () => TeardownCamPtr(ingress.CameraPointer)),
                Soft("dispatcher", () => TeardownRouter(ingress)),
                Soft("mouse source", () => TeardownPointerSrc(ingress.MouseSource)),
                Soft("keyboard source", () => TeardownKeyboardSrc(ingress.KeyboardSource)),
                Soft("native window callbacks", () => TeardownPaneHooks(ingress.WindowCallbacks)),
            ]),
            new AssetShutdownJuncture("plugin host",
            [
                Hard("plugins", () => ingress.Plugins?.Dispose()),
            ]),
            new AssetShutdownJuncture("frame borrowers",
            [
                Hard("world frame composition", () => cycle.FrameGraphPublication?.Dispose()),
                Hard("frame-root bindings", () => cycle.FrameBindings?.Dispose()),
                Hard("session/player bindings", () => cycle.SessionBindings?.Dispose()),
            ]),
            new AssetShutdownJuncture("session dependents",
            [
                Hard("interaction/UI late bindings", () => cycle.InteractionBindings?.Dispose()),
                Hard("mouse capture", () => online.CameraPointer?.FreePointerGazeFollowingSessSunset()),
                Hard("retail UI", () => TeardownCanonWidget(online.RetailUi)),
                Hard("magic runtime", () => online.Magic?.Dispose()),
                Hard("item interaction", () => online.ItemInteraction?.Dispose()),
                Hard("external containers", () => online.ExternalContainers?.Dispose()),
                Hard("streamer", () => online.Streamer?.Dispose()),
                Hard("equipped children", () => online.EquippedChildren?.Dispose()),
            ]),
            new AssetShutdownJuncture("live entities",
            [
                Hard("live entity runtime", () => online.LiveEntities?.Clear()),
                Hard(
                    "shadow render scene",
                    () => online.RenderSceneShadow?.Dispose()),
            ]),
            new AssetShutdownJuncture("effect dispatch edges",
            [
                Hard("live-presentation bindings", () => online.PresentationBindings?.Dispose()),
                Hard("entity-effect advance source", () =>
                {
                    if (online.EntityEffects is { } fxList)
                        online.EffectAdvance.Unbind(fxList);
                    online.EffectAdvance.Deactivate();
                }),
                Hard("animation-hook registrations", () => TeardownTapRegistrations(online.HookRegistrations)),
            ]),
            new AssetShutdownJuncture("live entity dependents",
            [
                Hard("live lights", () => online.LiveLights?.Dispose()),
                Hard("live presentation", () => online.LivePresentation?.Dispose()),
                Hard("effect network state", () =>
                {
                    online.EntityEffects?.WipeNetworkPhase();
                    online.AnimationHookFrames?.Clear();
                    online.EffectPoses.Clear();
                }),
                Hard("audio", () => TeardownSound(online.Audio)),
            ]),
            new AssetShutdownJuncture("submitted GPU work",
            [
                Hard("frame flight drain", () => rasterize.FrameFlights?.PauseForSubmittedJob()),
            ]),
            new AssetShutdownJuncture("render frontends",
            [
                Hard("portal tunnel", () =>
                {
                    rasterize.LocalTeleport?.Dispose();
                    rasterize.PortalTunnelFallback.FreeBackup();
                }),
                Hard("paperdoll viewport", () => rasterize.Paperdoll?.Dispose()),
                Hard(
                    "creature appraisal viewport",
                    () => rasterize.CreatureAppraisal?.Dispose()),
                Hard("chargen preview control", () => rasterize.ChargenPreviewController?.Dispose()),
                Hard("chargen preview viewport", () => rasterize.ChargenPreview?.Dispose()),
                Hard("summary preview control", () => rasterize.SummaryPreviewController?.Dispose()),
                Hard("summary preview viewport", () => rasterize.SummaryPreview?.Dispose()),
                Hard("mesh draw dispatcher", () => rasterize.DrawDispatcher?.Dispose()),
                Hard("environment cells", () => rasterize.EnvironmentCells?.Dispose()),
                Hard("portal depth mask", () => rasterize.PortalDepthMask?.Dispose()),
                Hard("clip frame", () => rasterize.ClipFrame?.Dispose()),
                Hard("sky", () => rasterize.Sky?.Dispose()),
                Hard("particles", () => rasterize.Particles?.Dispose()),
            ]),
            new AssetShutdownJuncture("game runtime root",
            [
                Hard("graphical runtime host lease", online.RuntimeHostLease.Dispose),
                Hard("game runtime", () => TeardownPlayCore(online.Runtime)),
            ]),
            new AssetShutdownJuncture("shared texture owners",
            [
                Hard("texture cache", () => rasterize.Textures?.Dispose()),
            ]),
            new AssetShutdownJuncture("mesh adapter",
            [
                Hard("WB mesh adapter", () => rasterize.MeshAdapter?.Dispose()),
            ]),
            new AssetShutdownJuncture("remaining render owners",
            [
                Hard("terrain", () => rasterize.Terrain?.Dispose()),
                Hard("scene lighting", () => rasterize.SceneLighting?.Dispose()),
                Hard("debug lines", () => rasterize.DebugLines?.Dispose()),
                Hard("text renderer", () => rasterize.TextRenderer?.Dispose()),
                Hard("debug font", () => rasterize.DebugFont?.Dispose()),
                Hard("frame pacing", rasterize.FramePacing.Dispose),
                Hard("frame profiler", rasterize.FrameProfiler.Dispose),
                Hard("GPU device (RHI)", () => rasterize.GpuDevice?.Dispose()),
            ]),
            new AssetShutdownJuncture("dedicated render resources",
            [
                Hard("terrain atlas", rasterize.DedicatedResources.FreeLandTileset),
            ]),
            new AssetShutdownJuncture("failed render construction cleanup",
            [
                Hard("resource construction ledger", rasterize.ConstructionCleanup.Dispose),
            ]),
            new AssetShutdownJuncture("frame flight owner",
            [
                Hard("frame flights", () => rasterize.FrameFlights?.Dispose()),
            ]),
            new AssetShutdownJuncture("content mappings",
            [
                Hard("prepared asset source", () => platform.PreparedAssets?.Dispose()),
                Hard("DAT collection", () => platform.Dats?.Dispose()),
            ]),
            new AssetShutdownJuncture("input context",
            [
                Hard("input context", () => platform.Input?.Dispose()),
            ]),
            new AssetShutdownJuncture("graphics API context",
            [
                Hard("graphics API", () => platform.Graphics?.Dispose()),
            ]));
    }
}
