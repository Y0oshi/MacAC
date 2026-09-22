using System.Globalization;

namespace MacAC.Mechanics.Gear;

/// <summary>The split-stack dialog's number: 1..stack size, driven by text or the slider.</summary>
public sealed class StackSplitGauge
{
    private const uint DialHops = 1000u;

    public uint Value { get; private set; } = 1u;

    public uint Ceiling { get; private set; } = 1u;

    public float Ratio => Value / (float)Ceiling;

    public event Action? Changed;

    public void Reset(uint pileDims, uint? startingVal = null)
    {
        uint ceiling = Math.Max(pileDims, 1u);
        Set(startingVal ?? ceiling, ceiling);
    }

    public void AssignFromPhrase(string? phrase)
    {
        SetValue(uint.TryParse(phrase, NumberStyles.None, CultureInfo.InvariantCulture, out uint num) ? num : 0u);
    }

    public void AssignFromDialRatio(float ratio)
    {
        uint thousandths = (uint)MathF.Truncate(Math.Clamp(ratio, 0f, 1f) * DialHops);
        SetValue(1u + (uint)((ulong)thousandths * Ceiling / DialHops));
    }

    public void SetValue(uint val) => Set(val, Ceiling);

    public uint FetchObjectDivideDims(uint objectIdent, uint chosenObjectIdent, uint objectPileDims) =>
        objectIdent == chosenObjectIdent ? Value : Math.Max(objectPileDims, 1u);

    private void Set(uint val, uint ceiling)
    {
        ceiling = Math.Max(ceiling, 1u);
        val = Math.Clamp(val, 1u, ceiling);
        if (Value == val && Ceiling == ceiling)
            return;
        Value = val;
        Ceiling = ceiling;
        Changed?.Invoke();
    }
}
