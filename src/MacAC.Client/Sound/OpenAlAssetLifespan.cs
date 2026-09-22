using MacAC.Client.Graphics;
using Silk.NET.OpenAL;

namespace MacAC.Client.Sound;

internal interface IOpenAlAssetApi
{
    AL? SoundApi { get; }
    ALContext? CtxApi { get; }

    nint OpenDev();
    nint BuildCtx(nint dev);
    bool CraftCtxLatest(nint ctx);
    uint ProduceSrc();
    void Configure3DSrc(uint src);
    void ConfigureWidgetSrc(uint src);
    void DeactivateAlGapAttenuation();
    void HaltSrc(uint src);
    void EraseSrc(uint src);
    void EraseBuf(uint buf);
    void DemolishCtx(nint ctx);
    void ShutDev(nint dev);
}

internal interface IOpenAlResourceApiMint
{
    IOpenAlAssetApi Create();
}

internal sealed class SilkOpenAlResourceApiMint : IOpenAlResourceApiMint
{
    public IOpenAlAssetApi Create() => new SilkOpenAlAssetApi();
}

internal sealed unsafe class SilkOpenAlAssetApi : IOpenAlAssetApi
{
    public SilkOpenAlAssetApi()
    {
        CtxApi = ALContext.GetApi(soft: true);
        SoundApi = AL.GetApi(soft: true);
    }

    public AL SoundApi { get; }
    public ALContext CtxApi { get; }

    public nint OpenDev() => (nint)CtxApi.OpenDevice(string.Empty);

    public nint BuildCtx(nint dev) =>
        (nint)CtxApi.CreateContext((Device*)dev, null);

    public bool CraftCtxLatest(nint ctx) =>
        CtxApi.MakeContextCurrent((Context*)ctx);

    public uint ProduceSrc() => SoundApi.GenSource();

    public void Configure3DSrc(uint src)
    {
        SoundApi.SetSourceProperty(src, SourceFloat.Gain, 1f);
        SoundApi.SetSourceProperty(src, SourceFloat.RolloffFactor, 0f);
        SoundApi.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
        SoundApi.SetSourceProperty(src, SourceBoolean.Looping, false);
    }

    public void ConfigureWidgetSrc(uint src)
    {
        SoundApi.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
        SoundApi.SetSourceProperty(src, SourceFloat.Gain, 1f);
        SoundApi.SetSourceProperty(src, SourceBoolean.Looping, false);
    }

    public void DeactivateAlGapAttenuation() =>
        SoundApi.DistanceModel(DistanceModel.None);

    public void HaltSrc(uint src) => SoundApi.SourceStop(src);

    public void EraseSrc(uint src) => SoundApi.DeleteSource(src);

    public void EraseBuf(uint buf) => SoundApi.DeleteBuffer(buf);

    public void DemolishCtx(nint ctx) =>
        CtxApi.DestroyContext((Context*)ctx);

    public void ShutDev(nint dev) =>
        CtxApi.CloseDevice((Device*)dev);
}

internal sealed class OpenAlAssetLifespan(IOpenAlAssetApi api) : IRetryableAssetTidy
{
    private sealed class OriginPhase(uint ident)
    {
        public uint Id { get; } = ident;
        public bool Released { get; set; }
    }

    private sealed class BufferLedger(uint ident)
    {
        public uint Id { get; } = ident;
        public bool Released { get; set; }
    }

    private readonly IOpenAlAssetApi _api = api ?? throw new ArgumentNullException(nameof(api));
    private readonly List<OriginPhase> _sources = [];
    private readonly List<BufferLedger> _bufs = [];
    private bool _ctxLatest;
    private bool _tidyEngaged;

    public nint Device { get; private set; }
    public nint Context { get; private set; }

    public bool IsTidyDone
    {
        get
        {
            return _sources.All(static src => src.Released)
        && _bufs.All(static buf => buf.Released)
        && Context == 0
        && Device == 0;
        }
    }

    public bool TryOpenDev()
    {
        if (Device != 0)
            throw new InvalidOperationException("The OpenAL device is by now open");
        Device = _api.OpenDev();
        return Device != 0;
    }

    public bool TryBuildCtx()
    {
        if (Device == 0)
            throw new InvalidOperationException("An OpenAL device is needed prior to its context");
        if (Context != 0)
            throw new InvalidOperationException("The OpenAL context by now exists");
        Context = _api.BuildCtx(Device);
        return Context != 0;
    }

    public bool TryCraftLatest()
    {
        if (Context == 0)
            throw new InvalidOperationException("An OpenAL context is needed prior to activation");
        _ctxLatest = _api.CraftCtxLatest(Context);
        return _ctxLatest;
    }

    public uint Create3DSrc()
    {
        uint src = _api.ProduceSrc();
        _sources.Add(new OriginPhase(src));
        _api.Configure3DSrc(src);
        return src;
    }

    public uint BuildWidgetSrc()
    {
        uint src = _api.ProduceSrc();
        _sources.Add(new OriginPhase(src));
        _api.ConfigureWidgetSrc(src);
        return src;
    }

    public void OwnBuf(uint buffer)
    {
        if (buffer is 0)
            throw new ArgumentOutOfRangeException(nameof(buffer));
        if (_bufs.Any(extant => extant.Id == buffer && !extant.Released))
            throw new InvalidOperationException($"OpenAL buffer {buffer} is by now owned");
        _bufs.Add(new BufferLedger(buffer));
    }

    public void FreeBuf(uint buf)
    {
        BufferLedger phase = _bufs.LastOrDefault(contender =>
            contender.Id == buf && !contender.Released)
            ?? throw new InvalidOperationException($"OpenAL buffer {buf} isn't owned");
        _api.EraseBuf(buf);
        phase.Released = true;
    }

    public void ReattemptTidy()
    {
        if (_tidyEngaged || IsTidyDone)
            return;

        _tidyEngaged = true;
        List<Exception>? misses = null;
        try
        {
            for (int idx = _sources.Count - 1; idx >= 0; --idx)
            {
                OriginPhase src = _sources[idx];
                if (src.Released)
                    continue;

                Exception? haltMiss = null;
                try
                {
                    _api.HaltSrc(src.Id);
                }
                catch (Exception miss)
                {
                    haltMiss = miss;
                }

                try
                {
                    _api.EraseSrc(src.Id);
                    src.Released = true;
                }
                catch (Exception miss)
                {
                    (misses ??= []).Add(new AggregateException(
                        $"OpenAL source {src.Id} could not be released",
                        haltMiss is null ? [miss] : [haltMiss, miss]));
                }
            }

            for (int idx = _bufs.Count - 1; idx >= 0; --idx)
            {
                var buf = _bufs[idx];
                if (buf.Released)
                    continue;
                try
                {
                    _api.EraseBuf(buf.Id);
                    buf.Released = true;
                }
                catch (Exception miss)
                {
                    (misses ??= []).Add(new InvalidOperationException(
                        $"OpenAL buffer {buf.Id} could not be released",
                        miss));
                }
            }

            bool descendantsReleased =
                _sources.All(static source => source.Released)
                && _bufs.All(static buffer => buffer.Released);
            if (descendantsReleased && Context != 0)
            {
                if (_ctxLatest)
                {
                    try
                    {
                        if (!_api.CraftCtxLatest(0))
                            throw new InvalidOperationException(
                                "OpenAL rejected clearing the current context");
                        _ctxLatest = false;
                    }
                    catch (Exception miss)
                    {
                        (misses ??= []).Add(new InvalidOperationException(
                            "The current OpenAL context could not be cleared",
                            miss));
                    }
                }

                if (!_ctxLatest)
                {
                    try
                    {
                        _api.DemolishCtx(Context);
                        Context = 0;
                    }
                    catch (Exception miss)
                    {
                        (misses ??= []).Add(new InvalidOperationException(
                            "The OpenAL context could not be destroyed",
                            miss));
                    }
                }
            }

            if (Context == 0 && Device != 0)
            {
                try
                {
                    _api.ShutDev(Device);
                    Device = 0;
                }
                catch (Exception miss)
                {
                    (misses ??= []).Add(new InvalidOperationException(
                        "The OpenAL device could not be closed",
                        miss));
                }
            }
        }
        finally
        {
            _tidyEngaged = false;
        }

        if (misses is not null)
            throw new AggregateException(
                "OpenAL native-resource cleanup remains incomplete",
                misses);
    }
}

internal sealed class OpenAlInitializationFault(
    Exception initializationMiss,
    OpenAlAssetLifespan lifetime,
    AggregateException tidyMiss) : AggregateException(
        "OpenAL initialization failed and native-resource cleanup remains incomplete.",
        [initializationMiss, .. tidyMiss.InnerExceptions]),
    IRetryableAssetTidy
{
    private readonly OpenAlAssetLifespan _lifespan = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

    public bool IsTidyDone => _lifespan.IsTidyDone;

    public void ReattemptTidy() => _lifespan.ReattemptTidy();
}
