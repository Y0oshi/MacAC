using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkCallException : InvalidOperationException
{
    internal VkCallException(string op, Result outcome)
        : base($"Vulkan call failed: {op} returned {outcome}.")
    {
        Operation = op;
        Result = outcome;
    }

    internal string Operation { get; }

    internal Result Result { get; }
}

internal static unsafe class VkInterop
{
    internal static void Check(Result outcome, string op)
    {
        if (outcome != Result.Success)
            throw new VkCallException(op, outcome);
    }

    // Read a NUL-terminated ASCII field out of a Vulkan struct
    internal static string ScanString(byte* val)
    {
        return val is null ? string.Empty : SilkMarshal.PtrToString((nint)val) ?? string.Empty;
    }

    internal static nint ReserveStringArr(IReadOnlyList<string> vals)
        => SilkMarshal.StringArrayToPtr(vals.ToArray());

    internal static void ReleaseStringArr(nint hnd)
    {
        if (hnd != 0)
            SilkMarshal.Free(hnd);
    }

    // Enumerate the instance extension names the loader advertises
    internal static IReadOnlyList<string> IterateInstExtensions(Silk.NET.Vulkan.Vk vk)
    {
        ArgumentNullException.ThrowIfNull(vk);
        uint tally = 0;
        Check(
            vk.EnumerateInstanceExtensionProperties((byte*)null, ref tally, null),
            "vkEnumerateInstanceExtensionProperties (count)");
        if (tally is 0)
            return [];

        ExtensionProperties[] props = new ExtensionProperties[tally];
        fixed (ExtensionProperties* lead = props)
        {
            Check(
                vk.EnumerateInstanceExtensionProperties((byte*)null, ref tally, lead),
                "vkEnumerateInstanceExtensionProperties");
        }

        return ScanLabels(props, tally);
    }

    // Enumerate the device extension names one physical device advertises
    internal static IReadOnlyList<string> IterateDevExtensions(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev)
    {
        ArgumentNullException.ThrowIfNull(vk);
        uint tally = 0;
        Check(
            vk.EnumerateDeviceExtensionProperties(dev, (byte*)null, ref tally, null),
            "vkEnumerateDeviceExtensionProperties (count)");
        if (tally is 0)
            return [];

        ExtensionProperties[] props = new ExtensionProperties[tally];
        fixed (ExtensionProperties* lead = props)
        {
            Check(
                vk.EnumerateDeviceExtensionProperties(dev, (byte*)null, ref tally, lead),
                "vkEnumerateDeviceExtensionProperties");
        }

        return ScanLabels(props, tally);
    }

    private static IReadOnlyList<string> ScanLabels(ExtensionProperties[] props, uint tally)
    {
        List<string> labels = new List<string>((int)tally);
        for (uint idx = 0; idx < tally && idx < props.Length; ++idx)
        {
            var listing = props[idx];
            labels.Add(ScanString(listing.ExtensionName));
        }

        labels.Sort(StringComparer.Ordinal);
        return labels;
    }
}

internal sealed unsafe class VkInstanceMint
{
    internal sealed record Built(
        Instance Instance,
        IReadOnlyList<string> EnabledExtensions,
        IReadOnlyList<string> UnavailableOptionalExtensions,
        uint ApiVersion);

    internal static Built Create(
        Silk.NET.Vulkan.Vk vk,
        IReadOnlyList<string> neededExtensions,
        bool activateOptionalExtensions)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(neededExtensions);

        var onHand = VkInterop.IterateInstExtensions(vk);
        var plan = VkExtensionPicking.Resolve(
            onHand,
            neededExtensions,
            VkExtensionPicking.LocateOptionalInstExtensions(
                activateOptionalExtensions));
        if (!plan.IsSatisfied)
        {
            throw new NotSupportedException(
                "The Vulkan loader doesn't advertise the needed instance " +
                $"extension(s): {string.Join(", ", plan.MissingRequired)}.");
        }

        uint apiVer = VulkanApiVer.Make(
            VkCapabilityRequirements.NeededApiMajor,
            VkCapabilityRequirements.NeededApiMinor,
            0);

        nint applicationLabel = SilkMarshal.StringToPtr("macac");
        nint engineLabel = SilkMarshal.StringToPtr("macac");
        nint extensionLabels = VkInterop.ReserveStringArr(plan.Enabled);
        try
        {
            ApplicationInfo application = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)applicationLabel,
                ApplicationVersion = VulkanApiVer.Make(0, 1, 0),
                PEngineName = (byte*)engineLabel,
                EngineVersion = VulkanApiVer.Make(0, 1, 0),
                ApiVersion = apiVer,
            };
            bool portability = plan.Enabled.Contains(
                VkExtensionPicking.PortabilityEnumerationExtension,
                StringComparer.Ordinal);
            InstanceCreateInfo build = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                Flags = portability
                    ? InstanceCreateFlags.EnumeratePortabilityBitKhr
                    : InstanceCreateFlags.None,
                PApplicationInfo = &application,
                EnabledExtensionCount = (uint)plan.Enabled.Count,
                PpEnabledExtensionNames = (byte**)extensionLabels,
                EnabledLayerCount = 0,
                PpEnabledLayerNames = null,
            };

            VkInterop.Check(
                vk.CreateInstance(&build, null, out Instance inst),
                "vkCreateInstance");
            return new Built(
                inst,
                plan.Enabled,
                plan.UnavailableOptional,
                apiVer);
        }
        finally
        {
            VkInterop.ReleaseStringArr(extensionLabels);
            SilkMarshal.Free(engineLabel);
            SilkMarshal.Free(applicationLabel);
        }
    }
}

internal static unsafe partial class VkPhysicalDeviceInspector
{
    internal static IReadOnlyList<VkPhysicalDeviceCandidate> Iterate(
        Silk.NET.Vulkan.Vk vk,
        Instance inst,
        out PhysicalDevice[] hnds)
    {
        ArgumentNullException.ThrowIfNull(vk);

        uint tally = 0;
        VkInterop.Check(
            vk.EnumeratePhysicalDevices(inst, ref tally, null),
            "vkEnumeratePhysicalDevices (count)");
        hnds = new PhysicalDevice[tally];
        if (tally is 0)
            return [];

        fixed (PhysicalDevice* lead = hnds)
        {
            VkInterop.Check(
                vk.EnumeratePhysicalDevices(inst, ref tally, lead),
                "vkEnumeratePhysicalDevices");
        }

        var contenders = new List<VkPhysicalDeviceCandidate>((int)tally);
        for (int idx = 0; idx < hnds.Length; ++idx)
        {
            PhysicalDeviceProperties props;
            vk.GetPhysicalDeviceProperties(hnds[idx], &props);
            contenders.Add(
                new VkPhysicalDeviceCandidate(
                    idx,
                    VkInterop.ScanString(props.DeviceName),
                    props.DeviceType,
                    props.ApiVersion,
                    props.DriverVersion,
                    props.VendorID,
                    props.DeviceID,
                    LargestDevOwnHeap(vk, hnds[idx])));
        }

        return contenders;
    }

    // Sum of the device-local heaps, which is the tie-break the plan specifies
    internal static ulong LargestDevOwnHeap(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev)
    {
        PhysicalDeviceMemoryProperties memory;
        vk.GetPhysicalDeviceMemoryProperties(dev, &memory);
        ulong sum = 0;
        for (uint idx = 0; idx < memory.MemoryHeapCount && idx < 16; ++idx)
        {
            MemoryHeap heap = memory.MemoryHeaps[(int)idx];
            if (heap.Flags.HasFlag(MemoryHeapFlags.DeviceLocalBit))
                sum += heap.Size;
        }

        return sum;
    }

    internal static uint HighestSpecimenTally(SampleCountFlags counts)
    {
        if (counts.HasFlag(SampleCountFlags.Count8Bit)) return 8;
        if (counts.HasFlag(SampleCountFlags.Count4Bit)) return 4;
        return counts.HasFlag(SampleCountFlags.Count2Bit) ? 2 : (uint)1;
    }

    internal static Format SelectZDepthStencilFmt(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev)
    {
        foreach (Format contender in new[] { Format.D32SfloatS8Uint, Format.D24UnormS8Uint })
        {
            FormatProperties props;
            vk.GetPhysicalDeviceFormatProperties(dev, contender, &props);
            if (props.OptimalTilingFeatures.HasFlag(
                    FormatFeatureFlags.DepthStencilAttachmentBit))

                return contender;
        }

        return Format.Undefined;
    }

    internal static bool SupportsOptimalSampling(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev,
        Format fmt)
    {
        FormatProperties props;
        vk.GetPhysicalDeviceFormatProperties(dev, fmt, &props);
        return props.OptimalTilingFeatures.HasFlag(FormatFeatureFlags.SampledImageBit);
    }

    // A short, stable driver identification string for bug reports
    internal static string DepictDriver(VkPhysicalDeviceCandidate dev)
    {
        ArgumentNullException.ThrowIfNull(dev);
        return
            $"vendor 0x{dev.VendorId:X4}, device 0x{dev.DeviceId:X4}, " +
            $"driver {VulkanApiVer.Major(dev.DriverVersion)}." +
            $"{VulkanApiVer.Minor(dev.DriverVersion)}." +
            $"{VulkanApiVer.Fix(dev.DriverVersion)} " +
            $"(raw 0x{dev.DriverVersion:X8})";
    }
}

internal sealed unsafe class VkLogicalDeviceMint
{
    internal sealed record ClientCreated(
        Device Device,
        Queue GraphicsQueue,
        Queue PresentQueue,
        VkQueueFamilyChoice Families,
        IReadOnlyList<string> EnabledExtensions,
        IReadOnlyList<string> UnavailableOptionalExtensions);

    internal static ClientCreated Create(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        VkQueueFamilyChoice clans,
        bool demandSwapchain,
        VkDeviceFeatureSupport onHandFeatures)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(clans);
        ArgumentNullException.ThrowIfNull(onHandFeatures);

        var onHand =
            VkInterop.IterateDevExtensions(vk, physicalDev);
        var plan = VkExtensionPicking.Resolve(
            onHand,
            demandSwapchain ? [VkExtensionPicking.SwapchainExtension] : [],
            VkExtensionPicking.OptionalDevExtensions);
        if (!plan.IsSatisfied)
        {
            throw new NotSupportedException(
                "The selected Vulkan device doesn't advertise the needed " +
                $"extension(s): {string.Join(", ", plan.MissingRequired)}.");
        }

        float precedence = 1f;
        uint[] uniqueClans = clans.IsUnified
            ? [clans.GraphicsFamily]
            : [clans.GraphicsFamily, clans.PresentFamily];
        DeviceQueueCreateInfo[] fifoCreates = new DeviceQueueCreateInfo[uniqueClans.Length];
        for (int idx = 0; idx < uniqueClans.Length; ++idx)
        {
            fifoCreates[idx] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueClans[idx],
                QueueCount = 1,
                PQueuePriorities = &precedence,
            };
        }

        var vulkan13 = new PhysicalDeviceVulkan13Features
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            DynamicRendering = true,
            Synchronization2 = true,
            Maintenance4 = true,
            ShaderDemoteToHelperInvocation = true,
        };
        var vulkan12 = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &vulkan13,
            DescriptorIndexing = true,
            RuntimeDescriptorArray = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingSampledImageUpdateAfterBind = true,
            DescriptorBindingUpdateUnusedWhilePending = true,
            DescriptorBindingVariableDescriptorCount = true,
            ShaderSampledImageArrayNonUniformIndexing = true,
            TimelineSemaphore = true,
            HostQueryReset = true,
        };
        var vulkan11 = new PhysicalDeviceVulkan11Features
        {
            SType = StructureType.PhysicalDeviceVulkan11Features,
            PNext = &vulkan12,
            ShaderDrawParameters = true,
            Multiview = onHandFeatures.Multiview,
        };
        PhysicalDeviceFeatures core = new PhysicalDeviceFeatures
        {
            MultiDrawIndirect = true,
            DrawIndirectFirstInstance = true,
            ShaderClipDistance = true,
            TextureCompressionBC = true,
            SamplerAnisotropy = true,
        };
        PhysicalDeviceFeatures2 features2 = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &vulkan11,
            Features = core,
        };

        nint extensionLabels = VkInterop.ReserveStringArr(plan.Enabled);
        try
        {
            fixed (DeviceQueueCreateInfo* fifos = fifoCreates)
            {
                DeviceCreateInfo build = new DeviceCreateInfo
                {
                    SType = StructureType.DeviceCreateInfo,
                    PNext = &features2,
                    QueueCreateInfoCount = (uint)fifoCreates.Length,
                    PQueueCreateInfos = fifos,
                    EnabledExtensionCount = (uint)plan.Enabled.Count,
                    PpEnabledExtensionNames = (byte**)extensionLabels,
                    PEnabledFeatures = null,
                };

                VkInterop.Check(
                    vk.CreateDevice(physicalDev, &build, null, out Device dev),
                    "vkCreateDevice");

                vk.GetDeviceQueue(dev, clans.GraphicsFamily, 0, out Queue visuals);
                Queue present = visuals;
                if (!clans.IsUnified)
                    vk.GetDeviceQueue(dev, clans.PresentFamily, 0, out present);

                return new ClientCreated(
                    dev,
                    visuals,
                    present,
                    clans,
                    plan.Enabled,
                    plan.UnavailableOptional);
            }
        }
        finally
        {
            VkInterop.ReleaseStringArr(extensionLabels);
        }
    }
}
