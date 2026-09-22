using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe partial class VkSwapchain : IDisposable
{
    private readonly Silk.NET.Vulkan.Vk _vk;

    private readonly KhrSurface _canvasApi;

    private readonly KhrSwapchain _swapchainApi;

    private readonly PhysicalDevice _physicalDev;

    private readonly Device _device;

    private readonly SurfaceKHR _canvas;

    private readonly VkQueueFamilyChoice _clans;

    private SwapchainKHR _swapchain;

    private Image[] _images = [];

    private ImageView[] _views = [];

    private Semaphore[] _renderComplete = [];

    private bool _destroyed;

    internal VkSwapchain(
        Silk.NET.Vulkan.Vk vk,
        KhrSurface surfaceApi,
        KhrSwapchain swapchainApi,
        PhysicalDevice physicalDev,
        Device dev,
        SurfaceKHR canvas,
        VkQueueFamilyChoice families)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _canvasApi = surfaceApi ?? throw new ArgumentNullException(nameof(surfaceApi));
        _swapchainApi = swapchainApi ?? throw new ArgumentNullException(nameof(swapchainApi));
        _physicalDev = physicalDev;
        _device = dev;
        _canvas = canvas;
        _clans = families ?? throw new ArgumentNullException(nameof(families));
    }
}
