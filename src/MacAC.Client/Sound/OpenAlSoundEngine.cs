using System.Numerics;
using MacAC.Mechanics.Sound;
using Silk.NET.OpenAL;

namespace MacAC.Client.Sound;

internal interface IRealmAudioQuiescence
{
    void SuspendRealmSound();
    void ReactivateRealmSound();
}

public sealed unsafe partial class OpenAlSoundEngine : IAudioBackend, IRealmAudioQuiescence
{
    private AL? _al;

    private OpenAlAssetLifespan? _assetList;
    private bool _destroyed;

    private const int ReservoirSize3D = 16;

    private const int ReservoirDimsWidget = 4;

    private sealed class Slot3D
    {
        public uint SourceId;
        public uint HolderIdent;
        public bool InUse;
        public float Priority;
    }

    private readonly Slot3D[] _pool3D = BuildRealmSockets();

    private int _pool3DCur; // round-robin start

    private bool _realmSoundSuspended;

    private readonly uint[] _reservoirWidget = new uint[ReservoirDimsWidget];

    private Vector3 _listenerLocus;

    private float _listenerBearingDeg;

    private const float UpperPanAzimuthDeg = 30f;

    internal const long DefaultBufByteAllowance = 48L * 1024 * 1024; // 48 MiB

    private readonly Dictionary<uint, uint> _bufByWaveIdent = [];

    private readonly AlBufferAllowanceLedger _bufAllowance = new(DefaultBufByteAllowance);

    public float MasterVolume { get; set; } = 1f;

    public float SfxVolume { get; set; } = 1f;

    public float AmbientVolume { get; set; } = 0.8f;

    public OpenAlSoundEngine()
        : this(new SilkOpenAlResourceApiMint())
    {
    }

    internal OpenAlSoundEngine(IOpenAlResourceApiMint apiMaker)
    {
        ArgumentNullException.ThrowIfNull(apiMaker);
        IOpenAlAssetApi api;
        try
        {
            api = apiMaker.Create();
        }
        catch
        {
            return;
        }

        _al = api.SoundApi;
        _assetList = new OpenAlAssetLifespan(api);
        try
        {
            if (!_assetList.TryOpenDev())

                return;
            if (!_assetList.TryBuildCtx())
            {
                DeactivateFollowingInitializationMiss(
                    new InvalidOperationException("OpenAL could not create a context"));
                return;
            }
            if (!_assetList.TryCraftLatest())
            {
                DeactivateFollowingInitializationMiss(
                    new InvalidOperationException("OpenAL could not activate its context"));
                return;
            }

            // Initialise 3D source pool
            for (int idx = 0; idx < ReservoirSize3D; ++idx)
            {
                uint src = _assetList.Create3DSrc();
                _pool3D[idx].SourceId = src;
            }

            // UI sources are source-relative (attached to listener) so they
            // ignore 3D position.
            for (int idx = 0; idx < ReservoirDimsWidget; ++idx)
            {
                uint src = _assetList.BuildWidgetSrc();
                _reservoirWidget[idx] = src;
            }

            api.DeactivateAlGapAttenuation();

            IsAvailable = true;
        }
        catch (OpenAlInitializationFault)
        {
            throw;
        }
        catch (Exception miss)
        {
            DeactivateFollowingInitializationMiss(miss);
        }
    }
}
