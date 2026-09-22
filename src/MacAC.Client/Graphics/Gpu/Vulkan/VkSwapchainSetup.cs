using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed record VkSwapchainSetup(
    Format ImageFormat,
    ColorSpaceKHR ColorSpace,
    PresentModeKHR PresentMode,
    uint ImageCount,
    uint Width,
    uint Height,
    ImageUsageFlags Usage,
    SurfaceTransformFlagsKHR PreTransform,
    CompositeAlphaFlagsKHR CompositeAlpha)
{
    internal bool IsPresentable => Width > 0 && Height > 0;
}

internal static class VkSwapchainSetupMint
{
    internal const Format PreferredFmt = Format.B8G8R8A8Unorm;

    internal const ColorSpaceKHR PreferredTintSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr;

    // Two frames in flight (plan §4.8), so three images is the working target: one presenting, one
    // queued, one being recorded
    internal const uint PreferredImageTally = 3;

    internal const ImageUsageFlags NeededUsage =
        ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit;

    internal static SurfaceFormatKHR SelectCanvasFmt(
        IReadOnlyList<SurfaceFormatKHR> onHand)
    {
        ArgumentNullException.ThrowIfNull(onHand);
        if (onHand.Count is 0)
            return new SurfaceFormatKHR(PreferredFmt, PreferredTintSpace);

        foreach (SurfaceFormatKHR fmt in onHand)
        {
            if (fmt.Format == PreferredFmt && fmt.ColorSpace == PreferredTintSpace)
                return fmt;
        }

        foreach (SurfaceFormatKHR fmt in onHand)
        {
            if (fmt.Format == PreferredFmt)
                return fmt;
        }

        return onHand[0];
    }

    // True when the surface offers the UNORM format the renderer requires
    internal static bool OffersUnormFmt(IReadOnlyList<SurfaceFormatKHR> onHand)
    {
        ArgumentNullException.ThrowIfNull(onHand);
        foreach (SurfaceFormatKHR fmt in onHand)
        {
            if (fmt.Format == PreferredFmt)
                return true;
        }

        return false;
    }

    internal static PresentModeKHR SelectPresentManner(
        FramePacingRule pacing,
        IReadOnlyList<PresentModeKHR> onHand)
    {
        ArgumentNullException.ThrowIfNull(onHand);

        if (pacing.UseVSync)
            return PresentModeKHR.FifoKhr;

        if (onHand.Contains(PresentModeKHR.ImmediateKhr))
            return PresentModeKHR.ImmediateKhr;
        if (onHand.Contains(PresentModeKHR.MailboxKhr))
            return PresentModeKHR.MailboxKhr;

        // FIFO is the only mode Vulkan guarantees is present
        return PresentModeKHR.FifoKhr;
    }

    internal static uint SelectImageTally(in SurfaceCapabilitiesKHR capabilities)
    {
        uint tally = Math.Max(PreferredImageTally, capabilities.MinImageCount);
        if (capabilities.MaxImageCount is not 0 && tally > capabilities.MaxImageCount)
            tally = capabilities.MaxImageCount;
        return tally;
    }

    internal static (uint Width, uint Height) SelectReach(
        in SurfaceCapabilitiesKHR capabilities,
        uint framebufferWidth,
        uint framebufferHeight)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
            return (capabilities.CurrentExtent.Width, capabilities.CurrentExtent.Height);

        uint width = Math.Clamp(
            framebufferWidth,
            capabilities.MinImageExtent.Width,
            capabilities.MaxImageExtent.Width);
        uint height = Math.Clamp(
            framebufferHeight,
            capabilities.MinImageExtent.Height,
            capabilities.MaxImageExtent.Height);
        return (width, height);
    }

    internal static SurfaceTransformFlagsKHR SelectPreXform(
        in SurfaceCapabilitiesKHR capabilities)
    {
        return capabilities.SupportedTransforms.HasFlag(SurfaceTransformFlagsKHR.IdentityBitKhr)
                ? SurfaceTransformFlagsKHR.IdentityBitKhr
                : capabilities.CurrentTransform;
    }

    internal static CompositeAlphaFlagsKHR SelectCompoundAlpha(
        in SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.SupportedCompositeAlpha.HasFlag(CompositeAlphaFlagsKHR.OpaqueBitKhr))
            return CompositeAlphaFlagsKHR.OpaqueBitKhr;
        return capabilities.SupportedCompositeAlpha.HasFlag(CompositeAlphaFlagsKHR.InheritBitKhr)
            ? CompositeAlphaFlagsKHR.InheritBitKhr
            : CompositeAlphaFlagsKHR.OpaqueBitKhr;
    }

    // True when the surface permits the TRANSFER_SRC usage screenshots need
    internal static bool SupportsTransferSrc(in SurfaceCapabilitiesKHR capabilities)
    {
        return capabilities.SupportedUsageFlags.HasFlag(ImageUsageFlags.TransferSrcBit);
    }

    internal static VkSwapchainSetup Create(
        in SurfaceCapabilitiesKHR capabilities,
        IReadOnlyList<SurfaceFormatKHR> formats,
        IReadOnlyList<PresentModeKHR> presentManners,
        FramePacingRule pacing,
        uint framebufferWidth,
        uint framebufferHeight)
    {
        var fmt = SelectCanvasFmt(formats);
        (uint width, uint height) = SelectReach(
            capabilities,
            framebufferWidth,
            framebufferHeight);

        ImageUsageFlags usage = SupportsTransferSrc(capabilities)
            ? NeededUsage
            : ImageUsageFlags.ColorAttachmentBit;

        return new VkSwapchainSetup(
            fmt.Format,
            fmt.ColorSpace,
            SelectPresentManner(pacing, presentManners),
            SelectImageTally(capabilities),
            width,
            height,
            usage,
            SelectPreXform(capabilities),
            SelectCompoundAlpha(capabilities));
    }
}

// What the frame loop must do after an acquire or a present returned
internal enum VkSwapchainAction
{
    Continue,

    // Rebuild the swapchain before doing anything else with it
    RecreateNow,

    // Usable this frame, but rebuild at the frame boundary
    RecreateAtFrameBoundary,

    // Nothing to present to - a minimised window
    Idle,

    Fail,
}

internal static class VkSwapchainRecreationRule
{
    internal static VkSwapchainAction OnObtain(Result outcome)
    {
        return outcome switch
        {
            Result.Success => VkSwapchainAction.Continue,
            Result.SuboptimalKhr => VkSwapchainAction.RecreateAtFrameBoundary,
            Result.ErrorOutOfDateKhr => VkSwapchainAction.RecreateNow,
            Result.Timeout or Result.NotReady => VkSwapchainAction.Idle,
            _ => VkSwapchainAction.Fail,
        };
    }

    internal static VkSwapchainAction OnPresent(Result outcome)
    {
        return outcome switch
        {
            Result.Success => VkSwapchainAction.Continue,
            Result.SuboptimalKhr => VkSwapchainAction.RecreateAtFrameBoundary,
            Result.ErrorOutOfDateKhr => VkSwapchainAction.RecreateNow,
            _ => VkSwapchainAction.Fail,
        };
    }

    internal static VkSwapchainAction OnFramebufferDims(uint width, uint height)
    {
        return width is 0 || height is 0
                ? VkSwapchainAction.Idle
                : VkSwapchainAction.Continue;
    }
}

internal static class VkBackbufferSwizzle
{
    // Swap the red and blue bytes of every 4-byte pixel in place
    internal static void SwapRedAndBlueInPlace(Span<byte> pixels)
    {
        if (pixels.Length % 4 is not 0)
        {
            throw new ArgumentException(
                "A BGRA/RGBA pixel span has to be a whole number of 4-byte pixels",
                nameof(pixels));
        }

        for (int idx = 0; idx + 3 < pixels.Length; idx += 4)
            (pixels[idx], pixels[idx + 2]) = (pixels[idx + 2], pixels[idx]);
    }

    internal static byte[] ToRgba(
        ReadOnlySpan<byte> src,
        int width,
        int height,
        int srcRankPitchOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(srcRankPitchOctets, width * 4);

        byte[] dest = new byte[width * height * 4];
        for (int y = 0; y < height; ++y)
        {
            var srcRank =
                src.Slice(y * srcRankPitchOctets, width * 4);
            Span<byte> destRank =
                dest.AsSpan(y * width * 4, width * 4);
            srcRank.CopyTo(destRank);
            SwapRedAndBlueInPlace(destRank);
        }

        return dest;
    }

    internal static byte[] ToGlOriginRgba(
        ReadOnlySpan<byte> src,
        int width,
        int height,
        int srcRankPitchOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(srcRankPitchOctets, width * 4);

        byte[] dest = new byte[width * height * 4];
        for (int y = 0; y < height; ++y)
        {
            var srcRank =
                src.Slice(y * srcRankPitchOctets, width * 4);
            Span<byte> destRank =
                dest.AsSpan((height - 1 - y) * width * 4, width * 4);
            srcRank.CopyTo(destRank);
            SwapRedAndBlueInPlace(destRank);
        }

        return dest;
    }
}
