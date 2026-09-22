namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal struct VkDrawWiringLedger
{
    private ulong _pipeArrangement;
    private int _bundleGen;
    private bool _hasMapping;
    private bool _stale;

    internal readonly bool RequiresBind(
        ulong pipeArrangement,
        int bundleGen)
    {
        return !_hasMapping
        || _stale
        || _pipeArrangement != pipeArrangement
        || _bundleGen != bundleGen;
    }

    internal void FlagStale() => _stale = true;

    internal void FlagTied(ulong pipeArrangement, int bundleGen)
    {
        _pipeArrangement = pipeArrangement;
        _bundleGen = bundleGen;
        _hasMapping = true;
        _stale = false;
    }
}
