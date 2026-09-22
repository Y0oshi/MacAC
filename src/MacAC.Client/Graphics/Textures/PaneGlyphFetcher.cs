using Silk.NET.Core;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacAC.Client.Graphics;

internal static class PaneGlyphFetcher
{
    private static readonly string[] ResourceNames =
    [
        "MacAC.Client.Graphics.Icons.macac-client-16.png",
        "MacAC.Client.Graphics.Icons.macac-client-32.png",
        "MacAC.Client.Graphics.Icons.macac-client-48.png",
        "MacAC.Client.Graphics.Icons.macac-client-256.png",
    ];

    private const string DockGlyphAsset = "MacAC.Client.Graphics.Icons.macac-client-512.png";

    public static void Apply(IWindow pane)
    {
        ArgumentNullException.ThrowIfNull(pane);

        if (!pane.IsInitialized)
        {
            Console.Error.WriteLine(
                "window icon: refusing to set an icon on an uninitialized window - "
                + "call WindowIconLoader.Apply from the Load callback, not next to "
                + "Window.Create.");
            return;
        }

        ImposeDockGlyph();

        RawImage[] images;
        try
        {
            images = Unpack();
        }
        catch (Exception miss)
        {
            Console.Error.WriteLine($"window icon: could not decode embedded icons - {miss}");
            return;
        }

        if (images.Length is 0)
        {
            Console.Error.WriteLine("window icon: no embedded icon resources found");
            return;
        }

        try
        {
            pane.SetWindowIcon(images.AsSpan());
        }
        catch (Exception miss)
        {
            // Wayland has no window-icon protocol and GLFW reports the request as unsupported there.
            Console.Error.WriteLine($"window icon: platform rejected the icon - {miss.Message}");
        }
    }

    // macOS shows the Dock icon from NSApplication, not from GLFW
    private static void ImposeDockGlyph()
    {
        if (!Machine.MacDockIcon.IsSupported)
            return;
        using Stream? flow = typeof(PaneGlyphFetcher).Assembly.GetManifestResourceStream(DockGlyphAsset);
        if (flow is null)
        {
            Console.Error.WriteLine($"dock icon: embedded resource absent - {DockGlyphAsset}");
            return;
        }
        ImposeDockGlyphRest(flow);
    }

    private static void ImposeDockGlyphRest(Stream flow)
    {
        using MemoryStream buf = new MemoryStream();
        flow.CopyTo(buf);
        Machine.MacDockIcon.Apply(buf.GetBuffer().AsSpan(0, (int)buf.Length));
    }

    private static RawImage[] Unpack()
    {
        var assembly = typeof(PaneGlyphFetcher).Assembly;
        List<RawImage> decoded = new List<RawImage>(ResourceNames.Length);

        foreach (string label in ResourceNames)
        {
            using Stream? flow = assembly.GetManifestResourceStream(label);
            if (flow is null)
            {
                Console.Error.WriteLine($"window icon: embedded resource absent - {label}");
                continue;
            }

            using Image<Rgba32> image = Image.Load<Rgba32>(flow);
            byte[] px = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(px);
            decoded.Add(new RawImage(image.Width, image.Height, px));
        }

        return [.. decoded];
    }
}
