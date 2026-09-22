using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Graphics.Batching
{
    internal sealed class BitmapTilesetTeardownTransaction
    {
        public bool IsComplete { get; private set; }
        public bool IsRunning { get; private set; }

        public void Advance(
            Action reattemptStratumRetirements,
            Action teardownTextureArr,
            Action sealLogicalDisposal)
        {
            ArgumentNullException.ThrowIfNull(reattemptStratumRetirements);
            ArgumentNullException.ThrowIfNull(teardownTextureArr);
            ArgumentNullException.ThrowIfNull(sealLogicalDisposal);
            if (IsComplete || IsRunning)
                return;

            IsRunning = true;
            try
            {
                reattemptStratumRetirements();
                teardownTextureArr();
                sealLogicalDisposal();
                IsComplete = true;
            }
            finally
            {
                IsRunning = false;
            }
        }
    }

    public class TextureAtlasKeeper : IDisposable
    {
        private static uint _upcomingSocket = 1;
        private readonly Dictionary<BitmapTag, int> _textureOrdinals = [];
        private readonly Dictionary<int, int> _refCounts = [];
        private readonly TextureAtlasSlotAllotter _sockets;
        private readonly BitmapTilesetStratumSunset _stratumSunset;
        private readonly BitmapTilesetTeardownTransaction _teardownTransaction = new();
        private readonly Action<TextureAtlasKeeper>? _onGpuSafeVacant;
        private bool _destroyed;
        internal const long MarkArrOctets = 8L * 1024 * 1024;
        internal const int CeilingArrStrata = 32;

        public uint Slot { get; }

        internal IRealmTextureArray TextureArr { get; private set; } = null!;

        public int ConsumedSockets => _textureOrdinals.Count;
        public int SumSockets => TextureArr?.Size ?? 0;
        public int OnHandSlots => _sockets.OnHandTally;
        internal bool IsGpuSafeVacant => ConsumedSockets is 0 && OnHandSlots == SumSockets;
        internal long AllocatedBytes => TextureArr.SumDimsInOctets;
        internal bool IsPhysicalSunsetDone =>
            TextureArr.IsPhysicalSunsetDone;
        internal long PreviousUseSeries { get; set; }
        private readonly int _textureWidth;

        internal int Width => _textureWidth;
        private readonly int _textureHeight;

        internal int Height => _textureHeight;
        private readonly TexelLayout _fmt;

        internal TexelLayout Format => _fmt;
        internal TextureAtlasKeeper(
            IRealmTextureArrayMint arrs,
            int width,
            int height,
            TexelLayout fmt = TexelLayout.RGBA8,
            Action<TextureAtlasKeeper>? onGpuSafeVacant = null)
        {
            ArgumentNullException.ThrowIfNull(arrs);
            Slot = _upcomingSocket++;
            _textureWidth = width;
            _textureHeight = height;
            _fmt = fmt;
            _onGpuSafeVacant = onGpuSafeVacant;
            _stratumSunset = new BitmapTilesetStratumSunset(arrs.Retirement);
            int cap = DeriveStartingCap(width, height, fmt);
            TextureArr = arrs.BuildClampedArr(fmt, width, height, cap);
            _sockets = new TextureAtlasSlotAllotter(TextureArr.Size);
        }

        public int AppendTexture(BitmapTag tag, byte[] blob, PushPixelFmt? pushPixelFmt = null, PushPixelKind? pushPixelKind = null)
        {
            ObjectDisposedException.ThrowIf(_destroyed || _teardownTransaction.IsRunning, this);
            _stratumSunset.ReattemptQueuedPublications();
            if (_textureOrdinals.TryGetValue(tag, out var extantOrdinal))
            {
                _refCounts[extantOrdinal]++;
                return extantOrdinal;
            }

            int ordinal = _sockets.Rent();

            try
            {
                TextureArr.RefreshStratum(ordinal, blob, pushPixelFmt, pushPixelKind);
                _textureOrdinals[tag] = ordinal;
                _refCounts[ordinal] = 1;
                return ordinal;
            }
            catch (Exception)
            {
                if (!_textureOrdinals.ContainsKey(tag))
                    _sockets.Yield(ordinal);
                throw;
            }
        }

        public void FreeTexture(BitmapTag tag)
        {
            ObjectDisposedException.ThrowIf(_destroyed || _teardownTransaction.IsRunning, this);
            _stratumSunset.ReattemptQueuedPublications();
            if (!_textureOrdinals.TryGetValue(tag, out var ordinal)) return;

            if (!_refCounts.ContainsKey(ordinal)) return;

            _refCounts[ordinal]--;
            if (_refCounts[ordinal] <= 0)
            {
                _textureOrdinals.Remove(tag);
                _refCounts.Remove(ordinal);
                _stratumSunset.Retire(
                    () =>
                    {
                        if (!_destroyed)
                            _sockets.Yield(ordinal);
                    },
                    () =>
                    {
                        if (!_destroyed && IsGpuSafeVacant)
                            _onGpuSafeVacant?.Invoke(this);
                    });
            }
        }

        public bool HasTexture(BitmapTag tag) => _textureOrdinals.ContainsKey(tag);

        public int FetchTextureOrdinal(BitmapTag tag) =>
            _textureOrdinals.TryGetValue(tag, out var ordinal) ? ordinal : -1;

        public void Dispose()
        {
            if (_destroyed) return;
            _teardownTransaction.Advance(
                _stratumSunset.ReattemptQueuedPublications,
                () =>
                {
                    TextureArr?.Dispose();
                    if (TextureArr is not null && !TextureArr.HasDurableTeardownOwnership)
                        throw new InvalidOperationException(
                            "Texture-array disposal returned without retaining or publishing its physical release");
                },
                () =>
                {
                    _textureOrdinals.Clear();
                    _refCounts.Clear();
                    _destroyed = true;
                });
        }

        internal static int DeriveStartingCap(int width, int height, TexelLayout fmt)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

            long octetsPerStratum = DeriveMipChainOctets(width, height, fmt);
            long markStrata = Math.Max(1L, MarkArrOctets / octetsPerStratum);
            return checked((int)Math.Min(markStrata, CeilingArrStrata));
        }

        internal static long DeriveMipChainOctets(int width, int height, TexelLayout fmt)
        {
            long sum = 0;
            int w = width;
            int h = height;
            while (true)
            {
                sum = checked(sum + DeriveTierOctets(w, h, fmt));
                if (w is 1 && h is 1) return sum;
                w = Math.Max(1, w >> 1);
                h = Math.Max(1, h >> 1);
            }
        }

        internal static long DeriveArrOctets(int width, int height, TexelLayout fmt)
        {
            return checked(DeriveMipChainOctets(width, height, fmt)
                * DeriveStartingCap(width, height, fmt));
        }

        internal static long DeriveTierOctets(int width, int height, TexelLayout fmt)
        {
            return fmt switch
            {
                TexelLayout.RGBA8 => checked((long)width * height * 4L),
                TexelLayout.RGB8 => checked((long)width * height * 3L),
                TexelLayout.A8 => checked((long)width * height),
                TexelLayout.Rgba32f => checked((long)width * height * 16L),
                TexelLayout.DXT1 => checked((long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8L),
                TexelLayout.DXT3 or TexelLayout.DXT5 => checked((long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16L),
                _ => throw new NotSupportedException($"Not supported texture-atlas format {fmt}.")
            };
        }

        internal void ReattemptQueuedRetirements() =>
            _stratumSunset.ReattemptQueuedPublications();
    }

    internal sealed class BitmapTilesetStratumSunset(IGpuAssetSunsetFifo fifo)
    {
        private readonly GpuSunsetRegister _register = new GpuSunsetRegister(fifo);

        internal int ExpectingBulletinTally => _register.ExpectingBulletinTally;

        public void Retire(Action returnStratum, Action alertGpuSafeVacant)
        {
            ArgumentNullException.ThrowIfNull(returnStratum);
            ArgumentNullException.ThrowIfNull(alertGpuSafeVacant);
            _register.Retire(new RetryableGpuAssetFree(
                returnStratum,
                alertGpuSafeVacant));
        }

        public void ReattemptQueuedPublications() =>
            _register.ReattemptPendingPublications();
    }
}
