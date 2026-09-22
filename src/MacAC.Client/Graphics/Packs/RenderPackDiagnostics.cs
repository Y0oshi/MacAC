using System.Numerics;

namespace MacAC.Client.Graphics.Packs;

internal readonly record struct RenderPackPassTelemetry(
    string PassId,
    double GpuMilliseconds,
    int DrawCalls,
    int DispatchCalls);

internal sealed record RenderPackEngineTelemetry(
    string EffectiveQuality,
    long RetainedGpuBytes,
    long TransientGpuBytes,
    int ImageCount,
    int BufferCount,
    int DrawCalls,
    int DispatchCalls,
    int ShadowCasterCount,
    int CascadeDrawCount,
    int CpuClassificationCalls,
    double SunElevationDegrees,
    int ActiveDayGroup,
    string Weather,
    double WeatherIntensity,
    bool Outdoor,
    double DirectionalShadowStrength,
    IReadOnlyList<RenderPackPassTelemetry> Passes)
{
    public uint SharedWorldTransformUsedInstances { get; init; }

    public IReadOnlyList<RenderPackCpuStageTelemetry> CpuJunctures { get; init; } = [];
    public AuthoredCelestialShadeSourceKind DirectedShadeSrcSort
    {
        get;
        init;
    }
    public int DirectedShadeSrcObjectOrdinal { get; init; } = -1;
    public uint DirectedShadeSrcGfxObjRefIdent { get; init; }
    public Vector3 DirectedShadeCanvasToLampDir { get; init; }
    public float DirectedShadeLampElevationSin { get; init; }
    public DirectionalShadeTransformChurnTelemetry ShadeXformChurn
    {
        get;
        init;
    }

    internal static RenderPackEngineTelemetry Empty(string fidelity)
    {
        return new(
        fidelity,
        RetainedGpuBytes: 0,
        TransientGpuBytes: 0,
        ImageCount: 0,
        BufferCount: 0,
        DrawCalls: 0,
        DispatchCalls: 0,
        ShadowCasterCount: 0,
        CascadeDrawCount: 0,
        CpuClassificationCalls: 0,
        SunElevationDegrees: 0,
        ActiveDayGroup: -1,
        Weather: "unknown",
        WeatherIntensity: 0,
        Outdoor: false,
        DirectionalShadowStrength: 0,
        Passes: []);
    }
}

internal interface IRenderPackEngineTelemetrySource
{
    RenderPackEngineTelemetry GrabTelemetry();
}

internal sealed record RenderPackTelemetryCapture(
    RenderPackActivationPhase State,
    string PackId,
    string? PackVersion,
    string PresetId,
    string EffectiveQuality,
    string? FailureReason,
    long ActivationGeneration,
    long RetainedGpuBytes,
    long TransientGpuBytes,
    int ImageCount,
    int BufferCount,
    int DrawCalls,
    int DispatchCalls,
    int ShadowCasterCount,
    int CascadeDrawCount,
    int CpuClassificationCalls,
    double SunElevationDegrees,
    int ActiveDayGroup,
    string Weather,
    double WeatherIntensity,
    bool Outdoor,
    double DirectionalShadowStrength,
    IReadOnlyList<RenderPackPassTelemetry> Passes,
    RenderPackPerformanceCapture Performance = default)
{
    public uint SharedWorldTransformUsedInstances { get; init; }

    public IReadOnlyList<RenderPackCpuStageTelemetry> CpuStages { get; init; } = [];
    public AuthoredCelestialShadeSourceKind DirectionalShadowSourceKind
    {
        get;
        init;
    }
    public int DirectionalShadowSourceObjectIndex { get; init; } = -1;
    public uint DirectionalShadowSourceGfxObjId { get; init; }
    public Vector3 DirectionalShadowSurfaceToLightDirection { get; init; }
    public float DirectionalShadowLightElevationSin { get; init; }
    public DirectionalShadeTransformChurnTelemetry ShadowTransformChurn
    {
        get;
        init;
    }

    internal static RenderPackTelemetryCapture Retail { get; } = new(
        RenderPackActivationPhase.Retail,
        PackId: "retail",
        PackVersion: null,
        PresetId: "off",
        EffectiveQuality: "off",
        FailureReason: null,
        ActivationGeneration: 0,
        RetainedGpuBytes: 0,
        TransientGpuBytes: 0,
        ImageCount: 0,
        BufferCount: 0,
        DrawCalls: 0,
        DispatchCalls: 0,
        ShadowCasterCount: 0,
        CascadeDrawCount: 0,
        CpuClassificationCalls: 0,
        SunElevationDegrees: 0,
        ActiveDayGroup: -1,
        Weather: "unknown",
        WeatherIntensity: 0,
        Outdoor: false,
        DirectionalShadowStrength: 0,
        Passes: []);

    internal bool IsCanon =>
        string.Equals(PackId, "retail", StringComparison.Ordinal);
}

internal interface IRenderPackTelemetryCaptureSource
{
    RenderPackTelemetryCapture SnapTelemetry();
}

internal sealed class DeferredRenderPackTelemetrySource
    : IRenderPackTelemetryCaptureSource
{
    private IRenderPackTelemetryCaptureSource? _mark;

    public RenderPackTelemetryCapture SnapTelemetry() =>
        _mark?.SnapTelemetry() ?? RenderPackTelemetryCapture.Retail;

    internal IDisposable BindOwned(IRenderPackTelemetryCaptureSource mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (_mark is not null && !ReferenceEquals(_mark, mark))
            throw new InvalidOperationException("Render-pack diagnostics are by now bound");
        _mark = mark;
        return new Binding(this, mark);
    }

    private void Loosen(IRenderPackTelemetryCaptureSource mark)
    {
        if (ReferenceEquals(_mark, mark))
            _mark = null;
    }

    private sealed class Binding(
        DeferredRenderPackTelemetrySource holder,
        IRenderPackTelemetryCaptureSource mark) : IDisposable
    {
        private DeferredRenderPackTelemetrySource? _holder = holder;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(mark);
    }
}

internal static class RenderPackTelemetryComposer
{
    internal static string Format(RenderPackTelemetryCapture val)
    {
        return $"[render-pack] state={val.State} "
            + $"pack={val.PackId}@{val.PackVersion ?? "(missing)"} "
            + $"preset={val.PresetId} effective={val.EffectiveQuality} "
            + $"generation={val.ActivationGeneration} "
            + $"gpuBytes={val.RetainedGpuBytes}/{val.TransientGpuBytes} "
            + $"resources={val.ImageCount}i/{val.BufferCount}b "
            + $"submit={val.DrawCalls}d/{val.DispatchCalls}c "
            + $"worldTransforms={val.SharedWorldTransformUsedInstances}used "
            + $"shadow={val.ShadowCasterCount}casters/{val.CascadeDrawCount}cascadeDraws/"
            + $"{val.CpuClassificationCalls}classify "
            + $"shadowSource={val.DirectionalShadowSourceKind}/"
            + $"obj{val.DirectionalShadowSourceObjectIndex}/"
            + $"0x{val.DirectionalShadowSourceGfxObjId:X8}/"
            + $"dir({Invariant(val.DirectionalShadowSurfaceToLightDirection.X, "F4")},"
            + $"{Invariant(val.DirectionalShadowSurfaceToLightDirection.Y, "F4")},"
            + $"{Invariant(val.DirectionalShadowSurfaceToLightDirection.Z, "F4")})/"
            + $"elevSin={Invariant(val.DirectionalShadowLightElevationSin, "F4")} "
            + $"atmosphere={Invariant(val.SunElevationDegrees, "F2")}deg/day{val.ActiveDayGroup}/"
            + $"{val.Weather}:{Invariant(val.WeatherIntensity, "F3")}/outdoor={val.Outdoor}/"
            + $"shadowStrength={Invariant(val.DirectionalShadowStrength, "F3")} "
            + $"perf=cpu-added:{Invariant(val.Performance.IncrementalCpuMillisecondsP50, "F3")}/"
            + $"{Invariant(val.Performance.IncrementalCpuMillisecondsP95, "F3")}/"
            + $"{Invariant(val.Performance.IncrementalCpuMillisecondsP99, "F3")}ms,"
            + $"receiver-cpu-absolute:{Invariant(val.Performance.AbsoluteReceiverCpuMillisecondsP50, "F3")}/"
            + $"{Invariant(val.Performance.AbsoluteReceiverCpuMillisecondsP95, "F3")}/"
            + $"{Invariant(val.Performance.AbsoluteReceiverCpuMillisecondsP99, "F3")}ms,"
            + $"gpu-inclusive:{Invariant(val.Performance.InclusiveGpuMillisecondsP50, "F3")}/"
            + $"{Invariant(val.Performance.InclusiveGpuMillisecondsP95, "F3")}/"
            + $"{Invariant(val.Performance.InclusiveGpuMillisecondsP99, "F3")}ms "
            + $"passes={ComposePasss(val.Passes)} "
            + $"cpuStages={ComposeCpuJunctures(val.CpuStages)} "
            + $"shadowTransformChurn={ComposeShadeXformChurn(val.ShadowTransformChurn)} "
            + $"reason={val.FailureReason ?? "none"}";
    }

    private static string ComposeShadeXformChurn(
        DirectionalShadeTransformChurnTelemetry val)
    {
        return $"scene={val.CopiedSceneChanges}[transform={val.UpdateTransformChanges},"
        + $"appearance={val.UpdateAppearanceChanges},sync={val.DynamicSynchronizationChanges};"
        + $"animated={val.ActiveAnimatedStaticChanges},live={val.LiveDynamicRootChanges},"
        + $"equipped={val.EquippedChildChanges}]/"
        + $"casters={val.DedupedCasterSlots}/sceneFallback={val.SceneJournalFullRefresh}/"
        + $"densityBulk={val.DensityBulkRefresh}/batchCopies={val.BatchedProjectionCopyCalls}/"
        + $"matrices={val.ChangedMatrixSlots}/flightCurrent={val.FlightCurrentChangedMatrices}/"
        + $"flightReplay={val.FlightPendingReplayMatrices}/uploaded={val.FlightUploadedMatrices}/"
        + $"ranges={val.FlightUploadRanges}/bytes={val.FlightBytesWritten}/"
        + $"flightFallback={val.FlightFullDynamicFallback}/denseDirect={val.DenseDirectUpload}/"
        + $"denseReplay={val.DenseFlightReplay}/"
        + $"classes=[terrain={val.CasterClasses.TerrainCommands},"
        + $"outdoorStatic={val.CasterClasses.OutdoorStatics},"
        + $"building={val.CasterClasses.Buildings},"
        + $"animated={val.CasterClasses.AnimatedStatics},"
        + $"localPlayer={val.CasterClasses.LocalPlayers},"
        + $"remotePlayer={val.CasterClasses.RemotePlayers},"
        + $"nonPlayerCreature={val.CasterClasses.NonPlayerCreatures},"
        + $"otherLive={val.CasterClasses.OtherLiveDynamics},"
        + $"equipped={val.CasterClasses.EquippedChildren}]";
    }

    private static string ComposePasss(IReadOnlyList<RenderPackPassTelemetry> passs)
    {
        return passs.Count is 0
            ? "none"
            : string.Join(
                ',',
                passs.Select(static pass =>
                    $"{pass.PassId}:{Invariant(pass.GpuMilliseconds, "F3")}ms/"
                    + $"{pass.DrawCalls}d/{pass.DispatchCalls}c"));
    }

    private static string ComposeCpuJunctures(
        IReadOnlyList<RenderPackCpuStageTelemetry> junctures)
    {
        return junctures.Count is 0
            ? "none"
            : string.Join(
                ',',
                junctures.Select(static juncture =>
                    $"{juncture.Stage}:{juncture.SampleCount}n/"
                    + $"{Invariant(juncture.CpuMillisecondsP50, "F3")}/"
                    + $"{Invariant(juncture.CpuMillisecondsP95, "F3")}/"
                    + $"{Invariant(juncture.CpuMillisecondsP99, "F3")}ms"));
    }

    private static string Invariant(double val, string fmt) =>
        val.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
}
