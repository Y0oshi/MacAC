using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static class VkRenderFailureRule
{
    internal static bool IsFatal(Exception problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        if (problem is AggregateException aggregate)
        {
            foreach (Exception interior in aggregate.Flatten().InnerExceptions)
            {
                if (IsFatal(interior))
                    return true;
            }
        }

        for (Exception? latest = problem; latest is not null; latest = latest.InnerException)
        {
            if (latest is OutOfMemoryException)
                return true;
            if (latest is VkCallException vulkan && IsFatal(vulkan.Result))
                return true;
        }
        return false;
    }

    private static bool IsFatal(Result outcome)
    {
        return outcome is
        Result.ErrorDeviceLost
        or Result.ErrorOutOfHostMemory
        or Result.ErrorOutOfDeviceMemory
        or Result.ErrorSurfaceLostKhr;
    }
}
