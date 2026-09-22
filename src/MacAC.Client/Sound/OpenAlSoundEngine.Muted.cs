using System.Numerics;
using MacAC.Mechanics.Sound;
using Silk.NET.OpenAL;

namespace MacAC.Client.Sound;

public sealed unsafe partial class OpenAlSoundEngine
{
    public bool Muted
    {
        get; set
        {
            field = value;
            if (IsAvailable && _al is not null)
                _al.SetListenerProperty(ListenerFloat.Gain, value ? 0f : 1f);
        }
    }

    public bool IsAvailable { get; private set; }

    internal bool IsDisposalComplete =>
        _destroyed || _assetList is null || _assetList.IsTidyDone;

    public void Dispose()
    {
        if (_destroyed)
            return;

        IsAvailable = false;
        _assetList?.ReattemptTidy();
        _destroyed = _assetList is null || _assetList.IsTidyDone;
    }

    public void AssignListener(float spotX, float spotY, float spotZ, float bearingDeg)
    {
        _listenerLocus = new Vector3(spotX, spotY, spotZ);
        _listenerBearingDeg = bearingDeg;
    }

    public bool Play3DWave(
        uint holderIdent,
        uint waveIdent,
        PcmClip wave,
        Vector3 locus,
        float volume,
        float precedence)
    {
        if (_realmSoundSuspended || !IsAvailable || _al is null) return false;

        var mix = CanonMixer.Mix(
            _listenerLocus,
            _listenerBearingDeg,
            locus,
            volume,
            FxMaster);
        if (!mix.Play) return false;

        uint buf = SecureBuf(waveIdent, wave);
        if (buf is 0) return false;

        int socketIndex = ObtainRealmSocket(precedence);
        if (socketIndex < 0) return false;    // nothing lower-priority - drop

        float gain = CanonMixer.LinearGain(mix.Decibels);
        Slot3D socket = _pool3D[socketIndex];
        _al.SourceStop(socket.SourceId);
        _al.SetSourceProperty(socket.SourceId, SourceInteger.Buffer, 0);  // detach old
        _al.SetSourceProperty(socket.SourceId, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(socket.SourceId, SourceFloat.Gain, gain);
        ImposePan(socket.SourceId, mix.Pan);
        _al.SetSourceProperty(socket.SourceId, SourceBoolean.Looping, false);
        _al.SourcePlay(socket.SourceId);

        socket.InUse = true;
        socket.HolderIdent = holderIdent;
        socket.Priority = precedence;
        _pool3DCur = CanonVoicePool.ProgressCur(socketIndex, ReservoirSize3D);
        return true;
    }

    public long HousedBufOctets => _bufAllowance.ResidentOctets;

    public int HousedBufTally => _bufAllowance.Count;

    public void Play3D(SfxId ident, float x, float y, float z) { /* handled via AudioHookSink */ }

    public void SuspendRealmSound()
    {
        _realmSoundSuspended = true;
        for (int idx = 0; idx < _pool3D.Length; ++idx)
            HaltRealmSocket(_pool3D[idx]);
    }

    public void ReactivateRealmSound() => _realmSoundSuspended = false;

    private float FxMaster => MasterVolume * SfxVolume;

    /// <summary>Play a raw PcmClip blob as a 2D UI sound (no falloff, ignores listener position).</summary>
    public bool PlayWidgetWave(uint waveIdent, PcmClip wave, float volume = 1f)
    {
        if (!IsAvailable || _al is null) return false;

        uint buf = SecureBuf(waveIdent, wave);
        if (buf is 0) return false;

        // UI pool: find a free source (first not-playing), else round-robin.
        int socketIndex = -1;
        for (int idx = 0; idx < ReservoirDimsWidget; ++idx)
        {
            if (!IsStillPlaying(_reservoirWidget[idx])) { socketIndex = idx; break; }
        }
        if (socketIndex < 0) socketIndex = 0; // always replace slot 0 as a last resort

        if (!CanonMixer.TryFetchAttenuation(0f, volume, FxMaster, out int decibels))
            return false;

        uint src = _reservoirWidget[socketIndex];
        _al.SourceStop(src);
        _al.SetSourceProperty(src, SourceInteger.Buffer, 0);
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceFloat.Gain, CanonMixer.LinearGain(decibels));
        _al.SourcePlay(src);
        return true;
    }

    public void PlayWidget(SfxId ident) { /* handled via AudioHookSink */ }

    public bool PlayAmbient3DWave(
        uint waveIdent,
        PcmClip wave,
        Vector3 locus,
        float volume,
        float precedence)
    {
        if (_realmSoundSuspended || !IsAvailable || _al is null) return false;

        var mix = CanonMixer.Mix(
            _listenerLocus,
            _listenerBearingDeg,
            locus,
            volume,
            AmbientMaster);
        if (!mix.Play) return false;

        uint buf = SecureBuf(waveIdent, wave);
        if (buf is 0) return false;

        int socketIndex = ObtainRealmSocket(precedence);
        if (socketIndex < 0) return false;

        Slot3D socket = _pool3D[socketIndex];
        _al.SourceStop(socket.SourceId);
        _al.SetSourceProperty(socket.SourceId, SourceInteger.Buffer, 0);
        _al.SetSourceProperty(socket.SourceId, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(
            socket.SourceId,
            SourceFloat.Gain,
            CanonMixer.LinearGain(mix.Decibels));
        ImposePan(socket.SourceId, mix.Pan);
        _al.SetSourceProperty(socket.SourceId, SourceBoolean.Looping, false);
        _al.SourcePlay(socket.SourceId);

        socket.InUse = true;
        socket.HolderIdent = 0;
        socket.Priority = precedence;
        _pool3DCur = CanonVoicePool.ProgressCur(socketIndex, ReservoirSize3D);
        return true;
    }

    public bool PlayAmbientFromMiddle(
        uint waveIdent,
        PcmClip wave,
        float volume,
        float precedence)
    {
        if (_realmSoundSuspended || !IsAvailable || _al is null) return false;

        if (!CanonMixer.TryFetchAttenuation(0f, volume, AmbientMaster, out int decibels))
            return false;

        uint buf = SecureBuf(waveIdent, wave);
        if (buf is 0) return false;

        int socketIndex = ObtainRealmSocket(precedence);
        if (socketIndex < 0) return false;

        Slot3D socket = _pool3D[socketIndex];
        _al.SourceStop(socket.SourceId);
        _al.SetSourceProperty(socket.SourceId, SourceInteger.Buffer, 0);
        _al.SetSourceProperty(socket.SourceId, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(
            socket.SourceId,
            SourceFloat.Gain,
            CanonMixer.LinearGain(decibels));
        ImposePan(socket.SourceId, 0);
        _al.SetSourceProperty(socket.SourceId, SourceBoolean.Looping, false);
        _al.SourcePlay(socket.SourceId);

        socket.InUse = true;
        socket.HolderIdent = 0;
        socket.Priority = precedence;
        _pool3DCur = CanonVoicePool.ProgressCur(socketIndex, ReservoirSize3D);
        return true;
    }

    internal void HaltAllForHolder(uint holderIdent)
    {
        if (holderIdent is 0)
            return;

        for (int idx = 0; idx < _pool3D.Length; ++idx)
        {
            Slot3D socket = _pool3D[idx];
            if (socket.InUse && socket.HolderIdent == holderIdent)
                HaltRealmSocket(socket);
        }
    }

    private bool IsBufAffixedToAnySrc(uint bufIdent)
    {
        if (_al is null) return false;

        for (int idx = 0; idx < ReservoirSize3D; ++idx)
        {
            if (IsSrcTiedTo(_pool3D[idx].SourceId, bufIdent)) return true;
        }
        for (int idx = 0; idx < ReservoirDimsWidget; ++idx)
        {
            if (IsSrcTiedTo(_reservoirWidget[idx], bufIdent)) return true;
        }
        return false;
    }

    private bool IsSrcTiedTo(uint srcIdent, uint bufIdent)
    {
        _al!.GetSourceProperty(srcIdent, GetSourceInteger.Buffer, out int affixed);
        return (uint)affixed == bufIdent;
    }

    private bool IsStillPlaying(uint srcIdent)
    {
        if (_al is null) return false;
        _al.GetSourceProperty(srcIdent, GetSourceInteger.SourceState, out int phase);
        return phase == (int)SourceState.Playing;
    }

    private void DeactivateFollowingInitializationMiss(Exception miss)
    {
        IsAvailable = false;
        if (_assetList is null)
            return;

        try
        {
            _assetList.ReattemptTidy();
        }
        catch (AggregateException tidyMiss)
        {
            throw new OpenAlInitializationFault(
                miss,
                _assetList,
                tidyMiss);
        }

        _al = null;
    }

    private int ObtainRealmSocket(float precedence)
    {
        Span<VoiceSlotPhase> sockets = stackalloc VoiceSlotPhase[ReservoirSize3D];
        for (int idx = 0; idx < ReservoirSize3D; ++idx)
        {
            Slot3D s = _pool3D[idx];
            sockets[idx] = new VoiceSlotPhase(
                Occupied: s.InUse,
                StillPlaying: s.InUse && IsStillPlaying(s.SourceId),
                Priority: s.Priority);
        }

        return CanonVoicePool.Acquire(sockets, _pool3DCur, precedence);
    }

    private void ImposePan(uint srcIdent, int pan)
    {
        float locus = CanonMixer.StereoLocusFromPan(pan);
        float azimuth = locus * UpperPanAzimuthDeg * (MathF.PI / 180f);
        _al!.SetSourceProperty(srcIdent, SourceBoolean.SourceRelative, true);
        _al.SetSourceProperty(
            srcIdent,
            SourceVector3.Position,
            MathF.Sin(azimuth),
            0f,
            -MathF.Cos(azimuth));
    }

    private void HaltRealmSocket(Slot3D socket)
    {
        if (IsAvailable && _al is not null && socket.SourceId is not 0)
        {
            _al.SourceStop(socket.SourceId);
            _al.SetSourceProperty(socket.SourceId, SourceInteger.Buffer, 0);
        }

        socket.HolderIdent = 0;
        socket.Priority = 0f;
        socket.InUse = false;
    }

    private float AmbientMaster => MasterVolume * AmbientVolume;

    private uint SecureBuf(uint waveIdent, PcmClip wave)
    {
        if (!IsAvailable || _al is null) return 0;
        if (_bufByWaveIdent.TryGetValue(waveIdent, out var extant))
        {
            if (extant is not 0)
                _bufAllowance.Touch(waveIdent);
            return extant;
        }

        uint buffer = _al.GenBuffer();
        _assetList!.OwnBuf(buffer);
        var fmt = ChooseFmt(wave);
        if (fmt == 0)
        {
            _assetList.FreeBuf(buffer);
            _bufByWaveIdent[waveIdent] = 0;
            return 0;
        }

        fixed (byte* p = wave.PcmOctets)
            _al.BufferData(buffer, fmt, p, wave.PcmOctets.Length, wave.SpecimenRate);

        _bufByWaveIdent[waveIdent] = buffer;
        _bufAllowance.CaptureBuilt(waveIdent, buffer, wave.PcmOctets.Length);
        EvictBufsOverAllowance(protectedBufIdent: buffer);
        return buffer;
    }

    private void EvictBufsOverAllowance(uint protectedBufIdent)
    {
        while (_bufAllowance.ResidentOctets > _bufAllowance.UpperOctets)
        {
            bool IsProtected(uint bufIdent) =>
                bufIdent == protectedBufIdent || IsBufAffixedToAnySrc(bufIdent);

            if (!_bufAllowance.TryEvictOldestUnprotected(
                    IsProtected, out uint evictedWaveIdent, out uint evictedBufIdent))

                break;

            _bufByWaveIdent.Remove(evictedWaveIdent);
            _assetList!.FreeBuf(evictedBufIdent);
        }
    }

    private static BufferFormat ChooseFmt(PcmClip clip)
    {
        return (ChannelCount: clip.LaneTally, BitsPerSample: clip.BitsetPerSpecimen) switch
        {
            (1, 8) => BufferFormat.Mono8,
            (1, 16) => BufferFormat.Mono16,
            (2, 8) => BufferFormat.Stereo8,
            (2, 16) => BufferFormat.Stereo16,
            _ => 0,
        };
    }

    private static Slot3D[] BuildRealmSockets()
    {
        Slot3D[] sockets = new Slot3D[ReservoirSize3D];
        for (int idx = 0; idx < sockets.Length; ++idx)
            sockets[idx] = new Slot3D();
        return sockets;
    }
}
