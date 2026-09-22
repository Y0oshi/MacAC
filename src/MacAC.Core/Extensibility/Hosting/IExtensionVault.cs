namespace MacAC.Extensibility.Hosting;

public interface IExtensionVault
{
    bool IsAvailable => false;

    string? ScanPhrase(string tag) => null;

    /// <summary>Keys beneath one relative prefix.</summary>
    IReadOnlyList<string> List(string stem) => Array.Empty<string>();

    void EmitPhrase(string tag, string substance) =>
        throw new NotSupportedException("Plugin storage is not available");

    bool Delete(string tag) => false;
}

/// <summary>A vault that holds nothing and accepts nothing.</summary>
public sealed class SealedVault : IExtensionVault
{
    public static SealedVault Instance { get; } = new();

    private SealedVault()
    {
    }
}
