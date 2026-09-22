using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkDebugNames : IDisposable
{
    private readonly Device _device;
    private readonly ExtDebugUtils? _api;
    private bool _destroyed;

    private VkDebugNames(Device dev, ExtDebugUtils? api)
    {
        _device = dev;
        _api = api;
    }

    // A naming sink that does nothing - used when the extension is absent and by tests
    internal static VkDebugNames Disabled { get; } = new(default, null);

    internal bool IsTurnedOn => _api is not null;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _api?.Dispose();
    }

    internal static VkDebugNames Create(
        Silk.NET.Vulkan.Vk vk,
        Instance inst,
        Device dev,
        IReadOnlyCollection<string> turnedOnInstExtensions)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(turnedOnInstExtensions);
        if (!turnedOnInstExtensions.Contains(VkExtensionPicking.DiagUtilsExtension))
            return Disabled;
        return !vk.TryGetInstanceExtension(inst, out ExtDebugUtils api) ? Disabled : new VkDebugNames(dev, api);
    }

    internal void LabelBuf(Buffer buf, string label) =>
        Name(ObjectType.Buffer, buf.Handle, label);

    internal void LabelImage(Image image, string label) =>
        Name(ObjectType.Image, image.Handle, label);

    internal void LabelImageLens(ImageView lens, string label) =>
        Name(ObjectType.ImageView, lens.Handle, label);

    internal void LabelSampler(Sampler sampler, string label) =>
        Name(ObjectType.Sampler, sampler.Handle, label);

    internal void LabelPipe(Pipeline pipe, string label) =>
        Name(ObjectType.Pipeline, pipe.Handle, label);

    internal void LabelDevMemory(DeviceMemory memory, string label) =>
        Name(ObjectType.DeviceMemory, memory.Handle, label);

    internal void CommenceCaption(CommandBuffer directives, string caption)
    {
        if (_api is null || _destroyed)
            return;

        nint phrase = SilkMarshal.StringToPtr(caption);
        try
        {
            DebugUtilsLabelEXT details = new DebugUtilsLabelEXT
            {
                SType = StructureType.DebugUtilsLabelExt,
                PLabelName = (byte*)phrase,
            };
            _api.CmdBeginDebugUtilsLabel(directives, &details);
        }
        finally
        {
            SilkMarshal.Free(phrase);
        }
    }

    internal void FinishCaption(CommandBuffer directives)
    {
        if (_api is null || _destroyed)
            return;
        _api.CmdEndDebugUtilsLabel(directives);
    }

    private void Name(ObjectType kind, ulong hnd, string label)
    {
        if (_api is null || _destroyed || hnd is 0 || string.IsNullOrEmpty(label))
            return;

        nint phrase = SilkMarshal.StringToPtr(label);
        try
        {
            var details = new DebugUtilsObjectNameInfoEXT
            {
                SType = StructureType.DebugUtilsObjectNameInfoExt,
                ObjectType = kind,
                ObjectHandle = hnd,
                PObjectName = (byte*)phrase,
            };
            _api.SetDebugUtilsObjectName(_device, &details);
        }
        finally
        {
            SilkMarshal.Free(phrase);
        }
    }
}
