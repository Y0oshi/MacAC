using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Surfaces;
using PixelFormatId =  MacAC.Dat.PixelLayout;
using SurfaceType =  MacAC.Dat.SkinBits;

namespace MacAC.Client.Graphics;

public sealed partial class BitmapStash
{
    internal int PossessedBindlessTextureTally => _compoundTextures?.EngagedAssetTally ?? 0;

    internal int TextureHolderTally => _compoundTextures?.HolderTally ?? 0;

    internal int StashedCompoundTextureTally => _compoundTextures?.StashedListingTally ?? 0;

    internal int StashedUnownedCompoundTally => _compoundTextures?.UnownedListingTally ?? 0;

    internal long StashedUnownedCompoundOctets => _compoundTextures?.UnownedOctets ?? 0;

    internal int StashedMoteTextureTally => _moteTextures?.ListingTally ?? 0;

    internal int StashedUnownedMoteTextureTally => _moteTextures?.UnownedListingTally ?? 0;

    internal long StashedUnownedMoteTextureOctets => _moteTextures?.UnownedOctets ?? 0;

    internal int CompoundTilesetTally => _compoundTextures?.TilesetTally ?? 0;

    internal long CompoundTilesetOctets => _compoundTextures?.AllocatedBytes ?? 0;

    internal int CompoundCyclePushTally => _compoundTextures?.CyclePushTally ?? 0;

    internal long CompoundCyclePushOctets => _compoundTextures?.CyclePushOctets ?? 0;

    internal bool CanBeginCompoundPush => _compoundTextures?.CanBeginPush == true;

    internal int EngagedMoteTextureTally => _moteTextures?.EngagedAssetTally ?? 0;

    internal int MoteTextureHolderTally => _moteTextures?.HolderTally ?? 0;

    public uint FetchOrPushRasterizeCanvas(uint rasterizeCanvasIdent, out int width, out int height, bool closest = false)
    {
        var stashTag = (renderSurfaceId: rasterizeCanvasIdent, nearest: closest);
        if (_rasterizeCanvasGpuTextures.TryGetValue(stashTag, out GpuWidgetTextureEntry extant))
        {
            width = extant.Width; height = extant.Height;
            return WidgetTextureChartHandle.FromSocket(extant.Slot);
        }

        UnpackedTexture decoded;
        if (_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out var surface)
            || _datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out surface))
        {
            ColorTable? swatch = surface.DefaultColorTableId is not 0
                ? _datFiles.Get<ColorTable>(surface.DefaultColorTableId)
                : null;
            decoded = CanvasUnpacker.DecodeRenderSurface(surface, swatch);
        }
        else
        {
            if (_loggedAbsentRasterizeCanvasIdents.Add(rasterizeCanvasIdent))
            {
                Console.WriteLine(
                    $"[UI] BitmapStash: RenderSurface 0x{rasterizeCanvasIdent:X8} wasn't "
                    + "found in Portal or HighRes - drawing the 1x1 magenta placeholder");
            }
            decoded = UnpackedTexture.Magenta;
        }

        var listing = PushWidgetTexture(decoded, closest, $"ui-rendersurface-0x{rasterizeCanvasIdent:X8}");
        _rasterizeCanvasGpuTextures[stashTag] = listing;
        width = decoded.Width; height = decoded.Height;
        return WidgetTextureChartHandle.FromSocket(listing.Slot);
    }

    public uint PushRgba8(byte[] rgba, int width, int height, bool closest = false)
    {
        var listing = PushWidgetTexture(
            new UnpackedTexture(rgba, width, height), closest, "ui-adhoc-rgba8");
        _adhocGpuTextures.Add(listing);
        return WidgetTextureChartHandle.FromSocket(listing.Slot);
    }

    public void FreeHolder(uint ownActorIdent) => SecureCompoundTexturesOnHand().RelinquishHolder(ownActorIdent);

    public void PulseCompoundTextureStash() => _compoundTextures?.Tick();

    public void PulseMoteTextureStash() => _moteTextures?.Tick();

    public void PulseCanvasHistogramPrintIfTurnedOn()
    {
        if (_canvasHistogramAlreadyDumped) return;
        if (!string.Equals(System.Environment.GetEnvironmentVariable("MACAC_DUMP_SURFACES"), "1", StringComparison.Ordinal)) return;
        ++_printCycleCounter;
        if (_printCycleCounter < 600) return;
        if (_pushMetadata.Count < 100) return;

        PrintCanvasHistogram();
        _canvasHistogramAlreadyDumped = true;
    }

    public void CommenceCompoundTextureCycle() =>
        _compoundTextures?.BeginFrame(_destUnveilPushPrecedence);

    public void Dispose()
    {
        _moteTextures?.Dispose();
        _compoundTextures?.Dispose();

        _swatchIndexedByTexture.Clear();

        foreach (uint twinHnd in _linearWidgetTwinHnds.Values)
            _device.FreeTextureSocket(WidgetTextureChartHandle.ToSocket(twinHnd));
        _linearWidgetTwinHnds.Clear();
        _closestWidgetTextureSrcs.Clear();

        foreach (GpuWidgetTextureEntry listing in _rasterizeCanvasGpuTextures.Values)
        {
            listing.Texture.Dispose();
            _device.FreeTextureSocket(listing.Slot);
            UntrackUploadedTexture(listing.GlName);
        }
        _rasterizeCanvasGpuTextures.Clear();

        foreach (GpuWidgetTextureEntry listing in _realmCanvasGpuTextures.Values)
        {
            listing.Texture.Dispose();
            _device.FreeTextureSocket(listing.Slot);
            UntrackUploadedTexture(listing.GlName);
        }
        _realmCanvasGpuTextures.Clear();

        foreach (GpuWidgetTextureEntry listing in _adhocGpuTextures)
        {
            listing.Texture.Dispose();
            _device.FreeTextureSocket(listing.Slot);
            UntrackUploadedTexture(listing.GlName);
        }
        _adhocGpuTextures.Clear();
    }

    internal void AssignDestUnveilPushPrecedence(bool turnedOn) =>
        _destUnveilPushPrecedence = turnedOn;

    internal void EnrollResidencySrcs(TenancyKeeper keeper)
    {
        ArgumentNullException.ThrowIfNull(keeper);
        if (_compoundTextures is not null)
        {
            keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
                TenancyDomain.CompositeTextures,
                _compoundTextures.GrabResidency));
        }
        if (_moteTextures is not null)
        {
            keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
                TenancyDomain.StandaloneTextures,
                GrabStandaloneResidency));
        }
    }

    internal GpuTextureSlot EnrollRealmCanvas(uint canvasIdent, bool repeat)
    {
        var tag = (surfaceId: canvasIdent, repeat);
        if (_realmCanvasGpuTextures.TryGetValue(tag, out GpuWidgetTextureEntry extant))
            return extant.Slot;

        var decoded = UnpackFromDatFiles(
            canvasIdent,
            origTextureOverride: null,
            swatchOverride: null);
        var listing = PushRealmCanvasTexture(
            decoded,
            repeat,
            $"world-surface-0x{canvasIdent:X8}{(repeat ? "-repeat" : "-clamp")}");
        _realmCanvasGpuTextures[tag] = listing;
        return listing.Slot;
    }

    internal uint FetchOrBuildLinearWidgetTwin(uint hnd)
    {
        if (!_closestWidgetTextureSrcs.TryGetValue(hnd, out IGpuBitmap? texture))
            return hnd;
        if (_linearWidgetTwinHnds.TryGetValue(hnd, out uint twin))
            return twin;

        IClientGpuSampler linearSampler = _device.BuildSampler(GpuSamplerSpec.RealmRepeat);
        var twinSocket = _device.EnrollTexture(texture, linearSampler);
        uint twinHnd = WidgetTextureChartHandle.FromSocket(twinSocket);
        _linearWidgetTwinHnds[hnd] = twinHnd;
        return twinHnd;
    }

    internal BindlessBitmapLocale FetchOrPushWithOrigTextureOverrideBindless(
        uint holderOwnIdent,
        uint canvasIdent,
        uint overrideOrigTextureIdent)
    {
        var composites = SecureCompoundTexturesOnHand();
        CompoundBitmapTag tag = new CompoundBitmapTag(
            CompoundBitmapFlavor.OriginalTextureOverride,
            canvasIdent,
            overrideOrigTextureIdent,
            Palette: default);
        if (composites.TryAcquire(holderOwnIdent, tag, out BindlessBitmapLocale extant))
            return extant;
        if (!composites.CanBeginPush)
            return default;
        (int width, int height) = LocateDecodedDimensions(canvasIdent, overrideOrigTextureIdent);
        if (!composites.CanReadyPush(width, height))
            return default;

        var decoded = UnpackFromDatFiles(
            canvasIdent,
            origTextureOverride: overrideOrigTextureIdent,
            swatchOverride: null,
            bakeAuthoredSeeThrough: true);
        return composites.TryAppendAndObtain(holderOwnIdent, tag, decoded, out BindlessBitmapLocale added)
            ? added
            : default;
    }

    internal BindlessBitmapLocale FetchOrPushWithSwatchOverrideBindless(
        uint holderOwnIdent,
        uint canvasIdent,
        uint? overrideOrigTextureIdent,
        SwatchOverride swatchOverride,
        SwatchCompoundPersona swatchPersona)
    {
        var composites = SecureCompoundTexturesOnHand();
        uint origBmpTag = overrideOrigTextureIdent ?? 0;
        CompoundBitmapTag tag = new CompoundBitmapTag(
            CompoundBitmapFlavor.PaletteComposite,
            canvasIdent,
            origBmpTag,
            swatchPersona);
        if (composites.TryAcquire(holderOwnIdent, tag, out BindlessBitmapLocale extant))
            return extant;
        if (!composites.CanBeginPush)
            return default;
        (int width, int height) = LocateDecodedDimensions(canvasIdent, overrideOrigTextureIdent);
        if (!composites.CanReadyPush(width, height))
            return default;

        var decoded = UnpackFromDatFiles(
            canvasIdent,
            origTextureOverride: overrideOrigTextureIdent,
            swatchOverride: swatchOverride,
            bakeAuthoredSeeThrough: true);
        return composites.TryAppendAndObtain(holderOwnIdent, tag, decoded, out BindlessBitmapLocale added)
            ? added
            : default;
    }

    internal static SwatchCompoundPersona FetchSwatchPersona(SwatchOverride swatch) =>
        new(swatch, DigestSwatchOverride(swatch));

    internal GpuTextureSlot ObtainMoteTexture(int spoutHnd, uint canvasIdent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spoutHnd);
        ArgumentOutOfRangeException.ThrowIfZero(canvasIdent);
        var textures = SecureMoteTexturesOnHand();
        uint holderIdent = checked((uint)spoutHnd);
        if (textures.TryAcquire(
                holderIdent,
                canvasIdent,
                out StandaloneBindlessBitmapAsset? extant))

            return extant.Slot;

        var decoded = UnpackFromDatFiles(
            canvasIdent,
            origTextureOverride: null,
            swatchOverride: null);

        return ObtainMoteTextureRhi(textures, holderIdent, canvasIdent, decoded);
    }

    internal void FreeMoteTextureHolder(int spoutHnd)
    {
        if (spoutHnd <= 0 || _moteTextures is null)
            return;
        _moteTextures.ReleaseHolder(checked((uint)spoutHnd));
    }

    internal bool IsSwatchIndexed(uint canvasIdent, uint? overrideOrigTextureIdent)
    {
        uint origBmpTag = overrideOrigTextureIdent ?? 0;
        var tag = (surfaceId: canvasIdent, origTexKey: origBmpTag);
        if (_swatchIndexedByTexture.TryGetValue(tag, out bool indexed))
            return indexed;

        Skin? canvas = _datFiles.Get<Skin>(canvasIdent);
        if (canvas is null || canvas.Bits.HasFlag(SurfaceType.Base1Solid))
            return _swatchIndexedByTexture[tag] = false;

        uint canvasTextureIdent = overrideOrigTextureIdent ?? (uint)canvas.TextureId;
        var texture = _datFiles.Get<SkinTexture>(canvasTextureIdent);
        if (texture is null || texture.BitmapIds.Count is 0)
            return _swatchIndexedByTexture[tag] = false;

        uint rasterizeCanvasIdent = (uint)texture.BitmapIds[0];
        if (!_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out Bitmap? rasterizeCanvas)
            && !_datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out rasterizeCanvas))
            return _swatchIndexedByTexture[tag] = false;

        indexed = rasterizeCanvas.Layout is PixelFormatId.PFID_P8 or PixelFormatId.PFID_INDEX16;
        _swatchIndexedByTexture[tag] = indexed;
        return indexed;
    }

    internal static ulong DigestSwatchOverride(SwatchOverride p)
    {
        ulong h = 0xCBF29CE484222325UL;  // FNV-1a offset basis
        const ulong prime = 0x100000001B3UL;
        h = (h ^ p.BasePaletteId) * prime;
        foreach (var span in p.SubPalettes)
        {
            h = (h ^ span.SubPaletteId) * prime;
            h = (h ^ span.Offset) * prime;
            h = (h ^ span.Length) * prime;
        }
        return h;
    }

    internal static ColorTable ConstructModifiedSwatch(
        ColorTable baseSwatch,
        IReadOnlyList<SwatchOverride.SubPaletteSpan> subSwatches,
        Func<uint, ColorTable?> locateSwatch)
    {
        ColorTable modified = new ColorTable();
        modified.Colors.AddRange(baseSwatch.Colors);

        foreach (SwatchOverride.SubPaletteSpan sub in subSwatches)
        {
            ColorTable? subPal = locateSwatch(sub.SubPaletteId);
            if (subPal is null) continue;

            int shift = sub.Offset << 3;
            int numcolors = (sub.Length is 0 ? 0x100 : sub.Length) << 3;
            int finish = shift + numcolors;

            for (int idx = shift; idx < finish; ++idx)
            {
                if (idx >= modified.Colors.Count || idx >= subPal.Colors.Count)
                    break;
                modified.Colors[idx] = subPal.Colors[idx];
            }
        }

        return modified;
    }

    private TenancyDomainCapture GrabStandaloneResidency()
    {
        var textures = SecureMoteTexturesOnHand();
        return new TenancyDomainCapture(
            TenancyDomain.StandaloneTextures,
            EntryCount: textures.ListingTally,
            OwnerCount: textures.HolderTally,
            Charges: new TenancyCharges(
                GpuResidentBytes: checked(
                    textures.AllocatedBytes - textures.RetiringBytes),
                RetiringBytes: textures.RetiringBytes),
            BudgetBytes: textures.AllowanceOctets);
    }

    private GpuWidgetTextureEntry PushRealmCanvasTexture(
        UnpackedTexture decoded,
        bool repeat,
        string diagLabel)
    {
        IGpuBitmap texture = _device.BuildTexture(new GpuBitmapSpec(
            diagLabel,
            GpuBitmapFlavor.Texture2D,
            GpuBitmapFmt.Rgba8Unorm,
            Width: decoded.Width,
            Height: decoded.Height,
            LayerCount: 1,
            MipLevelCount: 1));
        try
        {
            texture.Upload(0, 0, decoded.Rgba8);
            uint glLabel = PushAccountingLabel(texture);
            FollowUploadedTexture(glLabel, decoded.Width, decoded.Height);

            IClientGpuSampler sampler = _device.BuildSampler(
                repeat ? GpuSamplerSpec.RealmRepeat : GpuSamplerSpec.RealmClamp);
            var socket = _device.EnrollTexture(texture, sampler);
            return new GpuWidgetTextureEntry(texture, socket, glLabel, decoded.Width, decoded.Height);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private GpuWidgetTextureEntry PushWidgetTexture(UnpackedTexture decoded, bool closest, string diagLabel)
    {
        IGpuBitmap texture = _device.BuildTexture(new GpuBitmapSpec(
            diagLabel,
            GpuBitmapFlavor.Texture2D,
            GpuBitmapFmt.Rgba8Unorm,
            Width: decoded.Width,
            Height: decoded.Height,
            LayerCount: 1,
            MipLevelCount: 1));
        try
        {
            texture.Upload(0, 0, decoded.Rgba8);
            uint glLabel = PushAccountingLabel(texture);
            FollowUploadedTexture(glLabel, decoded.Width, decoded.Height);

            IClientGpuSampler sampler = _device.BuildSampler(closest ? WidgetClosestRepeat : GpuSamplerSpec.RealmRepeat);
            var socket = _device.EnrollTexture(texture, sampler);
            uint hnd = WidgetTextureChartHandle.FromSocket(socket);
            if (closest)

                _closestWidgetTextureSrcs[hnd] = texture;
            return new GpuWidgetTextureEntry(texture, socket, glLabel, decoded.Width, decoded.Height);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private uint PushAccountingLabel(IGpuBitmap texture) => _upcomingSyntheticPushLabel--;

    private GpuTextureSlot ObtainMoteTextureRhi(
        StandaloneBindlessTextureShelf textures,
        uint holderIdent,
        uint canvasIdent,
        UnpackedTexture decoded)
    {
        IGpuBitmap texture = _device.BuildTexture(new GpuBitmapSpec(
            $"particle-surface-0x{canvasIdent:X8}",
            GpuBitmapFlavor.Texture2D,
            GpuBitmapFmt.Rgba8Unorm,
            Width: decoded.Width,
            Height: decoded.Height,
            LayerCount: 1,
            MipLevelCount: 1));
        try
        {
            texture.Upload(0, 0, decoded.Rgba8);
            uint accountingLabel = PushAccountingLabel(texture);
            FollowUploadedTexture(accountingLabel, decoded.Width, decoded.Height);

            IClientGpuSampler sampler = _device.BuildSampler(GpuSamplerSpec.RealmClamp);
            var socket = _device.EnrollTexture(texture, sampler);
            textures.AppendAndObtain(holderIdent, new StandaloneBindlessBitmapAsset
            {
                SurfaceId = canvasIdent,
                Name = accountingLabel,
                Texture = texture,
                Slot = socket,
                Bytes = checked((long)decoded.Width * decoded.Height * 4L),
            });
            return socket;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private (int Width, int Height) LocateDecodedDimensions(
        uint canvasIdent,
        uint? overrideOrigTextureIdent)
    {
        var tag = (surfaceId: canvasIdent, overrideOrigTextureIdent ?? 0);
        if (_decodedDimensionsByTexture.TryGetValue(tag, out var stashed))
            return stashed;

        Skin? canvas = _datFiles.Get<Skin>(canvasIdent);
        if (canvas is null
            || canvas.Bits.HasFlag(SurfaceType.Base1Solid)
            || (uint)canvas.TextureId is 0)
            return _decodedDimensionsByTexture[tag] = (1, 1);

        uint canvasTextureIdent = overrideOrigTextureIdent ?? (uint)canvas.TextureId;
        var texture = _datFiles.Get<SkinTexture>(canvasTextureIdent);
        if (texture is null || texture.BitmapIds.Count is 0)
            return _decodedDimensionsByTexture[tag] = (1, 1);

        uint rasterizeCanvasIdent = (uint)texture.BitmapIds[0];
        if ((!_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out Bitmap? rasterizeCanvas)
                && !_datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out rasterizeCanvas))
            || rasterizeCanvas.Width <= 0
            || rasterizeCanvas.Height <= 0
            || rasterizeCanvas.Pixels is null)
            return _decodedDimensionsByTexture[tag] = (1, 1);

        return _decodedDimensionsByTexture[tag] = (rasterizeCanvas.Width, rasterizeCanvas.Height);
    }

    private CompositeTextureArrayShelf SecureCompoundTexturesOnHand()
    {
        return _compoundTextures ?? throw new InvalidOperationException(
            "This BitmapStash owns no composite texture array cache");
    }

    private StandaloneBindlessTextureShelf SecureMoteTexturesOnHand()
    {
        return _moteTextures ?? throw new InvalidOperationException(
            "This BitmapStash owns no standalone particle texture cache");
    }

    private void PrintCanvasHistogram()
    {
        try
        {
            PrintCanvasHistogramCore();
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine($"[N6-DUMP] Could not write surface histogram: {exc.Message}");
        }
    }

    private void PrintCanvasHistogramCore()
    {
        System.IO.Directory.CreateDirectory(_telemetryFolder);
        string outTrail = System.IO.Path.Combine(
            _telemetryFolder,
            "n6-surfaces.txt");

        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"# macac surface-format histogram — generated {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        builder.AppendLine("# Per-entry: surfaceId(hex), width, height, format, byteCount");
        builder.AppendLine();

        HashSet<uint> observed = new HashSet<uint>();
        long sumOctets = 0;
        var binsByDim = new Dictionary<(int W, int H), int>();
        var binsByFmt = new Dictionary<string, int>();
        var binsByTriple = new Dictionary<(int W, int H, string F), int>();

        void Write(uint canvasIdent, uint label)
        {
            if (!observed.Add(label)) return;
            if (!_pushMetadata.TryGetValue(label, out var meta)) return;
            int octets = meta.Width * meta.Height * 4;
            sumOctets += octets;
            builder.AppendLine($"0x{canvasIdent:X8}, {meta.Width}, {meta.Height}, {meta.Format}, {octets}");

            var dimTag = (meta.Width, meta.Height);
            binsByDim[dimTag] = binsByDim.GetValueOrDefault(dimTag) + 1;
            binsByFmt[meta.Format] = binsByFmt.GetValueOrDefault(meta.Format) + 1;
            var tripleTag = (meta.Width, meta.Height, meta.Format);
            binsByTriple[tripleTag] = binsByTriple.GetValueOrDefault(tripleTag) + 1;
        }

        _moteTextures?.TourListings(asset => Write(asset.SurfaceId, asset.Name));
        _compoundTextures?.TourListings((canvasIdent, width, height) =>
        {
            int octets = checked(width * height * 4);
            sumOctets += octets;
            builder.AppendLine($"0x{canvasIdent:X8}, {width}, {height}, RGBA8_COMPOSITE_LAYER, {octets}");
            binsByDim[(width, height)] = binsByDim.GetValueOrDefault((width, height)) + 1;
            binsByFmt["RGBA8_COMPOSITE_LAYER"] =
                binsByFmt.GetValueOrDefault("RGBA8_COMPOSITE_LAYER") + 1;
            binsByTriple[(width, height, "RGBA8_COMPOSITE_LAYER")] =
                binsByTriple.GetValueOrDefault((width, height, "RGBA8_COMPOSITE_LAYER")) + 1;
        });

        builder.AppendLine();
        builder.AppendLine("# Rollups");
        builder.AppendLine($"# Total unique GL textures: {observed.Count}");
        builder.AppendLine($"# Total bytes (sum of W*H*4): {sumOctets}");

        builder.AppendLine("# Top 10 (W,H) dimension buckets:");
        foreach (var kv in binsByDim.OrderByDescending(kv => kv.Value).Take(10))
            builder.AppendLine($"#   {kv.Key.W}x{kv.Key.H}: {kv.Value}");

        builder.AppendLine("# Format buckets:");
        foreach (var kv in binsByFmt.OrderByDescending(kv => kv.Value))
            builder.AppendLine($"#   {kv.Key}: {kv.Value}");

        builder.AppendLine("# Top 10 (W,H,format) triples — atlas-opportunity input:");
        foreach (var kv in binsByTriple.OrderByDescending(kv => kv.Value).Take(10))
            builder.AppendLine($"#   {kv.Key.W}x{kv.Key.H} {kv.Key.F}: {kv.Value}");

        System.IO.File.WriteAllText(outTrail, builder.ToString());
        Console.WriteLine($"[N6-DUMP] Surface histogram written to {outTrail} ({observed.Count} textures, {sumOctets} bytes)");
    }

    private UnpackedTexture UnpackFromDatFiles(
        uint canvasIdent,
        uint? origTextureOverride,
        SwatchOverride? swatchOverride,
        bool bakeAuthoredSeeThrough = false)
    {
        Skin? canvas = _datFiles.Get<Skin>(canvasIdent);
        if (canvas is null)
        {
            Console.WriteLine($"[tex-miss] Surface 0x{canvasIdent:X8} -> magenta (thread={System.Environment.CurrentManagedThreadId})");
            return UnpackedTexture.Magenta;
        }

        if (canvas.Bits.HasFlag(SurfaceType.Base1Solid) || (uint)canvas.TextureId is 0)
            return CanvasUnpacker.UnpackSolidTint(canvas.Color, canvas.Translucency);

        uint canvasTextureIdent = origTextureOverride ?? (uint)canvas.TextureId;
        SkinTexture? canvasTexture = _datFiles.Get<SkinTexture>(canvasTextureIdent);
        if (canvasTexture is null || canvasTexture.BitmapIds.Count is 0)
        {
            Console.WriteLine($"[tex-miss] SurfaceTexture 0x{canvasTextureIdent:X8} (surface 0x{canvasIdent:X8}) -> magenta (thread={System.Environment.CurrentManagedThreadId})");
            return UnpackedTexture.Magenta;
        }

        uint rasterizeCanvasIdent = (uint)canvasTexture.BitmapIds[0];
        if (!_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out var rs)
            && !_datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out rs))
        {
            Console.WriteLine($"[tex-miss] RenderSurface 0x{rasterizeCanvasIdent:X8} (surface 0x{canvasIdent:X8}) -> magenta (thread={System.Environment.CurrentManagedThreadId})");
            return UnpackedTexture.Magenta;
        }

        ColorTable? baseSwatch = rs.DefaultColorTableId is not 0
            ? _datFiles.Get<ColorTable>(rs.DefaultColorTableId)
            : null;

        ColorTable? netSwatch = baseSwatch;
        if (swatchOverride is not null && baseSwatch is not null && swatchOverride.SubPalettes.Count > 0)

            netSwatch = ConstructSwatch(baseSwatch, swatchOverride);

        bool isClipLookup = canvas.Bits.HasFlag(SurfaceType.Base1ClipMap);
        bool isAdditive = canvas.Bits.HasFlag(SurfaceType.Additive);

        UnpackedTexture decoded =
            CanvasUnpacker.DecodeRenderSurface(rs, netSwatch, isClipLookup, isAdditive);

        if (bakeAuthoredSeeThrough
            && canvas.Translucency > 0.0f
            && !ReferenceEquals(decoded, UnpackedTexture.Magenta))
        {
            decoded = CanvasUnpacker.ImposeAuthoredSeeThrough(decoded, canvas.Translucency);
        }

        return decoded;
    }

    private ColorTable ConstructSwatch(ColorTable baseSwatch, SwatchOverride swatchOverride)
    {
        return ConstructModifiedSwatch(
                baseSwatch,
                swatchOverride.SubPalettes,
                ident => _datFiles.Get<ColorTable>(ident));
    }

    private void FollowUploadedTexture(uint label, int width, int height)
    {
        _pushMetadata[label] = (width, height, "RGBA8_DECODED");
        long octets = checked((long)width * height * 4L);
        Batching.GpuMemoryLedger.FollowAssetAlloc(Batching.GpuAssetKind.Texture);
        Batching.GpuMemoryLedger.FollowAlloc(octets, Batching.GpuAssetKind.Texture);
    }

    private void UntrackUploadedTexture(uint label)
    {
        if (_pushMetadata.Remove(label, out var metadata))
        {
            long octets = checked((long)metadata.Width * metadata.Height * 4L);
            Batching.GpuMemoryLedger.FollowDeallocation(octets, Batching.GpuAssetKind.Texture);
            Batching.GpuMemoryLedger.FollowAssetDeallocation(Batching.GpuAssetKind.Texture);
        }
    }
}
