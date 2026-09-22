using System.Text.Json;
using MacAC.Client.Graphics.Packs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacAC.Client.Telemetry;

internal sealed class FrameScreenshotDriver
{
    private enum GrabPhase
    {
        Pending,
        Complete,
        Failed,
    }

    private sealed record GrabCondition(GrabPhase State, string? Error = null);

    private readonly Func<int, int, byte[]> _scanRgba;
    private readonly string _directory;
    private readonly Action<string> _trace;
    private readonly Func<RenderPackTelemetryCapture>? _rasterizeBundleMetadata;
    private readonly Queue<string> _queued = new();
    private readonly Dictionary<string, GrabCondition> _condition =
        new(StringComparer.OrdinalIgnoreCase);

    internal FrameScreenshotDriver(
        Func<int, int, byte[]> readRgba,
        string directory,
        Action<string>? trace = null,
        Func<RenderPackTelemetryCapture>? rasterizeBundleMetadata = null)
    {
        _scanRgba = readRgba ?? throw new ArgumentNullException(nameof(readRgba));
        _directory = string.IsNullOrWhiteSpace(directory)
            ? throw new ArgumentException("A screenshot directory is needed", nameof(directory))
            : Path.GetFullPath(directory);
        _trace = trace ?? (_ => { });
        _rasterizeBundleMetadata = rasterizeBundleMetadata;
    }

    public bool TryReq(string label, out string problem)
    {
        if (!AutopilotArtifactName.TryVet(label, out problem))
            return false;

        if (_condition.TryGetValue(label, out GrabCondition? condition))
        {
            if (condition.State != GrabPhase.Failed)
                return true;
            problem = condition.Error ?? $"screenshot '{label}' failed";
            return false;
        }

        _condition.Add(label, new GrabCondition(GrabPhase.Pending));
        _queued.Enqueue(label);
        _trace($"[world-gate] screenshot-request name={label}");
        return true;
    }

    public bool TryReqCanonScreenshot(out string trail, out string problem)
    {
        for (int ordinal = 0; ordinal < 100_000; ++ordinal)
        {
            string label = $"ScreenShot{ordinal:D5}";
            string contender = Path.Combine(_directory, label + ".png");
            if (File.Exists(contender) || _condition.ContainsKey(label))
                continue;

            if (TryReq(label, out problem))
            {
                trail = contender;
                return true;
            }

            trail = string.Empty;
            return false;
        }

        trail = string.Empty;
        problem = "all retail screenshot names ScreenShot00000 through ScreenShot99999 are in use";
        return false;
    }

    public bool IsComplete(string label)
    {
        return _condition.TryGetValue(label, out GrabCondition? condition)
        && condition.State == GrabPhase.Complete;
    }

    public bool SnapQueued(int width, int height)
    {
        if (_queued.Count is 0)
            return false;

        string label = _queued.Dequeue();
        try
        {
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"not valid framebuffer size {width}x{height}");

            byte[] px = _scanRgba(width, height);
            int anticipated = checked(width * height * 4);
            if (px.Length != anticipated)
                throw new InvalidOperationException(
                    $"framebuffer read returned {px.Length} bytes; wanted {anticipated}");

            byte[] flipped = FlipRanks(px, width, height);
            Directory.CreateDirectory(_directory);
            string trail = Path.Combine(_directory, label + ".png");
            string temporaryTrail = trail + ".tmp";
            using (var image = Image.LoadPixelData<Rgba32>(flipped, width, height))
                image.SaveAsPng(temporaryTrail);

            string? metadataTrail = null;
            string? temporaryMetadataTrail = null;
            if (_rasterizeBundleMetadata is not null)
            {
                metadataTrail = Path.Combine(_directory, label + ".metadata.json");
                temporaryMetadataTrail = metadataTrail + ".tmp";
                CycleScreenshotMetadata metadata = new CycleScreenshotMetadata(
                    SchemaVersion: 1,
                    Width: width,
                    Height: height,
                    RenderPack: _rasterizeBundleMetadata());
                File.WriteAllBytes(
                    temporaryMetadataTrail,
                    JsonSerializer.SerializeToUtf8Bytes(
                        metadata,
                        new JsonSerializerOptions { WriteIndented = true }));
            }

            if (metadataTrail is not null && temporaryMetadataTrail is not null)
                File.Move(temporaryMetadataTrail, metadataTrail, overwrite: true);
            File.Move(temporaryTrail, trail, overwrite: true);

            _condition[label] = new GrabCondition(GrabPhase.Complete);
            _trace($"[world-gate] screenshot-complete name={label} path={trail} size={width}x{height}");
            return true;
        }
        catch (Exception exception)
        {
            TryErase(Path.Combine(_directory, label + ".png.tmp"));
            TryErase(Path.Combine(_directory, label + ".metadata.json.tmp"));
            TryErase(Path.Combine(_directory, label + ".metadata.json"));
            string msg = $"screenshot '{label}' failed: {exception.Message}";
            _condition[label] = new GrabCondition(GrabPhase.Failed, msg);
            _trace($"[world-gate] screenshot-failed name={label} error={exception.Message}");
            return false;
        }
    }

    internal static byte[] FlipRanks(byte[] px, int width, int height)
    {
        int stride = checked(width * 4);
        byte[] flipped = new byte[px.Length];
        for (int rank = 0; rank < height; ++rank)
        {
            System.Buffer.BlockCopy(
                px,
                rank * stride,
                flipped,
                (height - 1 - rank) * stride,
                stride);
        }
        return flipped;
    }

    internal static byte[] ScanDefaultFramebuffer(
        IDefaultFramebufferCanvas canvas,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        byte[] px = new byte[checked(width * height * 4)];
        uint earlierScan = canvas.ScanFramebufferMapping;
        uint earlierPaint = canvas.PaintFramebufferMapping;
        canvas.AttachScanFramebuffer(0u);
        canvas.AttachPaintFramebuffer(0u);
        try
        {
            if (canvas.DefaultFramebufferSpecimens > 1)
                LocateThenScan(canvas, width, height, px);
            else
                canvas.ScanRgba(width, height, px);
        }
        finally
        {
            canvas.AttachScanFramebuffer(earlierScan);
            canvas.AttachPaintFramebuffer(earlierPaint);
        }
        return px;
    }

    internal interface IDefaultFramebufferCanvas
    {
        uint ScanFramebufferMapping { get; }

        uint PaintFramebufferMapping { get; }

        // GL_SAMPLES for the default framebuffer
        int DefaultFramebufferSpecimens { get; }

        void AttachScanFramebuffer(uint framebuffer);

        void AttachPaintFramebuffer(uint framebuffer);

        uint BuildLocateMark(int width, int height);

        void EraseLocateMark(uint framebuffer);

        void BlitTintClosest(int width, int height);

        void ScanRgba(int width, int height, byte[] dest);
    }

    private static void TryErase(string trail)
    {
        try
        {
            File.Delete(trail);
        }
        catch (Exception problem) when (problem is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
        }
    }

    private static void LocateThenScan(
        IDefaultFramebufferCanvas canvas,
        int width,
        int height,
        byte[] px)
    {
        uint locate = canvas.BuildLocateMark(width, height);
        try
        {
            // Read is still framebuffer 0 â€” the multisampled source
            canvas.AttachPaintFramebuffer(locate);
            canvas.BlitTintClosest(width, height);
            canvas.AttachScanFramebuffer(locate);
            canvas.ScanRgba(width, height, px);
        }
        finally
        {
            canvas.EraseLocateMark(locate);
        }
    }

}

internal sealed record CycleScreenshotMetadata(
    int SchemaVersion,
    int Width,
    int Height,
    RenderPackTelemetryCapture RenderPack);
