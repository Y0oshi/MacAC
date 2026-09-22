using System.Numerics;
using MacAC.Client.Shell;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkRetainedWidgetStage : IDisposable
{
    internal const int PreambleLeft = 24;
    internal const int PreambleTop = 18;
    internal const int PreambleWidth = 420;
    internal const int PreambleHeight = 96;

    private readonly MutableCycleOrigin _cycles = new();
    private readonly WidgetHub _hub;
    private readonly DiagStrokePainter _strokes;
    private readonly BitmapFont? _typeface;
    private readonly List<IGpuBitmap> _textures = [];

    private bool _destroyed;

    internal VkRetainedWidgetStage(IClientGpuDevice dev, string shaderFolder)
    {
        ArgumentNullException.ThrowIfNull(dev);

        byte[]? ttf = BitmapFont.TryPullSysMonospaceTypeface();
        _typeface = ttf is null ? null : new BitmapFont(dev, ttf, pixelHeight: 18f);

        _hub = new WidgetHub(dev, _cycles, shaderFolder, _typeface);
        _strokes = new DiagStrokePainter(dev, _cycles, shaderFolder);

        uint chrome = BuildTexture(dev, "vk-ui-chrome", AssembleChromeTile(), 16, 16, closest: false);
        uint glyph = BuildTexture(dev, "vk-ui-icon", AssembleGlyph(), 32, 32, closest: true);

        AssembleTree(chrome, glyph);
    }

    // True when a system font was found; without one no glyph draws happen
    internal bool HasTypeface => _typeface is not null;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _strokes.Dispose();
        _hub.Dispose();
        _typeface?.Dispose();
        for (int idx = _textures.Count - 1; idx >= 0; --idx)
            _textures[idx].Dispose();
        _textures.Clear();
    }

    internal void Render(IGpuCycle cycle, uint width, uint height, double secs)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        _cycles.LatestCycle = cycle;
        try
        {
            _strokes.Begin();
            float pass = (float)Math.Sin(secs) * 0.05f;
            _strokes.AppendStroke(new Vector3(-0.92f, -0.30f, 0f), new Vector3(-0.30f, -0.68f, 0f), new Vector3(1f, 0.85f, 0.2f));
            _strokes.AppendStroke(new Vector3(-0.30f, -0.68f, 0f), new Vector3(0.34f, -0.36f + pass, 0f), new Vector3(0.2f, 1f, 0.6f));
            _strokes.Flush(Matrix4x4.Identity, Matrix4x4.Identity);

            _hub.Tick(0.016);
            _hub.Draw(new Vector2(width, height));
        }
        finally
        {
            _cycles.LatestCycle = null;
        }
    }

    private void AssembleTree(uint chrome, uint glyph)
    {
        WidgetBoard preamble = new WidgetBoard
        {
            Name = "vk-header",
            Left = PreambleLeft,
            Top = PreambleTop,
            Width = PreambleWidth,
            Height = PreambleHeight,
            BackgroundSprite = 1,
            SpriteResolve = _ => (chrome, 16, 16),
        };
        preamble.AddChild(new WidgetCaption
        {
            Name = "vk-title",
            Left = 12,
            Top = 10,
            Width = 380,
            Height = 22,
            Text = "macac - retained UI on Vulkan",
            PhraseColor = new Vector4(1f, 0.94f, 0.72f, 1f),
        });
        preamble.AddChild(new WidgetCaption
        {
            Name = "vk-subtitle",
            Left = 12,
            Top = 34,
            Width = 380,
            Height = 22,
            Text = "top-left origin",
            PhraseColor = new Vector4(0.75f, 0.9f, 1f, 1f),
        });

        WidgetBoard interior = new WidgetBoard
        {
            Name = "vk-inner",
            Left = 12,
            Top = 58,
            Width = 260,
            Height = 28,
            BackgroundColor = new Vector4(0.05f, 0.08f, 0.16f, 0.85f),
            BorderTint = new Vector4(0.6f, 0.75f, 1f, 1f),
            BorderThickness = 2f,
        };
        interior.AddChild(new WidgetCaption
        {
            Left = 8,
            Top = 6,
            Width = 240,
            Height = 18,
            Text = "fill + border + glyphs",
            PhraseColor = new Vector4(1f, 1f, 1f, 1f),
        });
        preamble.AddChild(interior);

        WidgetTextureElement badge = new WidgetTextureElement
        {
            Name = "vk-icon",
            Left = PreambleLeft + PreambleWidth + 16,
            Top = PreambleTop + 40,
            Width = 64,
            Height = 64,
            Texture = glyph,
        };

        _hub.Root.AddChild(preamble);
        _hub.Root.AddChild(badge);
    }

    private uint BuildTexture(IClientGpuDevice dev, string label, byte[] rgba, int width, int height, bool closest)
    {
        IGpuBitmap texture = dev.BuildTexture(new GpuBitmapSpec(
            label,
            GpuBitmapFlavor.Texture2D,
            GpuBitmapFmt.Rgba8Unorm,
            width,
            height,
            LayerCount: 1,
            MipLevelCount: 1));
        _textures.Add(texture);
        texture.Upload(0, 0, rgba);
        IClientGpuSampler sampler = dev.BuildSampler(closest
            ? GpuSamplerSpec.WidgetClosest
            : GpuSamplerSpec.RealmRepeat);
        return WidgetTextureChartHandle.FromSocket(dev.EnrollTexture(texture, sampler));
    }

    private static byte[] AssembleChromeTile()
    {
        const int reach = 16;
        byte[] px = new byte[reach * reach * 4];
        for (int y = 0; y < reach; ++y)
        {
            for (int x = 0; x < reach; ++x)
            {
                bool rim = x is 0 || y is 0;
                byte r = rim ? (byte)0xC8 : (byte)0x2A;
                byte g = rim ? (byte)0xA0 : (byte)0x24;
                byte b = rim ? (byte)0x40 : (byte)0x1C;
                int shift = ((y * reach) + x) * 4;
                px[shift + 0] = r;
                px[shift + 1] = g;
                px[shift + 2] = b;
                px[shift + 3] = 0xF0;
            }
        }

        return px;
    }

    // A 32x32 badge: red at the top, blue at the bottom, opaque ring
    private static byte[] AssembleGlyph()
    {
        const int reach = 32;
        byte[] px = new byte[reach * reach * 4];
        for (int y = 0; y < reach; ++y)
        {
            for (int x = 0; x < reach; ++x)
            {
                float dx = (x - 15.5f) / 15.5f;
                float dy = (y - 15.5f) / 15.5f;
                bool inside = (dx * dx) + (dy * dy) <= 1f;
                int shift = ((y * reach) + x) * 4;
                px[shift + 0] = (byte)(inside ? 255 - (y * 6) : 0);
                px[shift + 1] = (byte)(inside ? 60 : 0);
                px[shift + 2] = (byte)(inside ? y * 7 : 0);
                px[shift + 3] = (byte)(inside ? 255 : 0);
            }
        }

        return px;
    }

    private sealed class MutableCycleOrigin : ILatestGpuCycleOrigin
    {
        public IGpuCycle? LatestCycle { get; set; }
    }
}
