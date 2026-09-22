using MacAC.Client.Graphics.Gpu.Vulkan;

namespace MacAC.Client.Rigging;

internal abstract class PlayPaneVisuals : IDisposable
{
    public virtual VkGraphicsScope? Vulkan => null;

    // The backend's world-pass seam
    public virtual MacAC.Client.Graphics.IRealmPassScope? RealmPassAmbit => null;

    public abstract void Dispose();
}

internal sealed class VkGameWindowGraphics(VkGraphicsScope context) : PlayPaneVisuals
{
    public VkGraphicsScope Ctx { get; } = context ?? throw new ArgumentNullException(nameof(context));

    public override VkGraphicsScope? Vulkan => Ctx;

    public VkRealmPassScope RealmPassAmbitCore { get; } = new VkRealmPassScope(context.SampleCount);

    public override MacAC.Client.Graphics.IRealmPassScope? RealmPassAmbit =>
        RealmPassAmbitCore;

    public override void Dispose() => Ctx.Dispose();
}
