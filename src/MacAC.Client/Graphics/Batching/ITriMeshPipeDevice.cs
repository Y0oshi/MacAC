namespace MacAC.Client.Graphics.Batching;

internal interface ITriMeshPipeDevice : IDisposable
{
    // Frame-flight-gated release for everything the pipeline allocates
    IGpuAssetSunsetFifo ResourceRetirement { get; }

    // The shared per-instance attribute buffer the legacy draw path binds
    uint InstanceVBO { get; }

    // GL_ARB_bindless_texture
    bool HasBindless { get; }

    // GL 4.3 or better
    bool HasOpenGL43 { get; }

    // True while deferred device work is still queued
    bool HasPendingWork { get; }

    void ProcessQueue();
}
