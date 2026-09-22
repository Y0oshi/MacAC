using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Assets.Pak;

namespace MacAC.Client.Rigging;

internal sealed class PreparedSetupPicker(
    IBakedAssetSource preparedAssets,
    Func<uint, RigSpec?> loadSetup,
    Action<string> diagnostic)
{
    private readonly IBakedAssetSource _preparedAssets = preparedAssets
            ?? throw new ArgumentNullException(nameof(preparedAssets));
    private readonly Func<uint, RigSpec?> _pullRig = loadSetup
            ?? throw new ArgumentNullException(nameof(loadSetup));
    private readonly Action<string> _diagnostic = diagnostic
            ?? throw new ArgumentNullException(nameof(diagnostic));
    private readonly ConcurrentDictionary<uint, byte> _diagnosed = new();

    public bool TryResolve(
        uint srcIdent,
        [NotNullWhen(true)] out RigSpec? rig)
    {
        var presence =
            _preparedAssets.Probe(PakAssetKind.SetupMesh, srcIdent);
        if (presence == BakedAssetPresence.Missing)
        {
            rig = null;
            return false;
        }
        if (presence == BakedAssetPresence.Corrupt)
        {
            throw new InvalidDataException(
                $"Prepared Setup entry 0x{srcIdent:X8} is corrupt; " +
                "activation can't safely infer presentation metadata");
        }

        try
        {
            rig = _pullRig(srcIdent);
            if (rig is not null)
                return true;

            DiagnoseOnce(
                srcIdent,
                $"Prepared Setup entry 0x{srcIdent:X8} exists but its " +
                "matching DAT record could not be loaded; activation skipped.");
            return false;
        }
        catch (InvalidDataException exc)
        {
            return DiagnoseCorruptBlob(srcIdent, exc, out rig);
        }
        catch (EndOfStreamException exc)
        {
            return DiagnoseCorruptBlob(srcIdent, exc, out rig);
        }
        catch (ArgumentOutOfRangeException exc)
        {
            return DiagnoseCorruptBlob(srcIdent, exc, out rig);
        }
    }

    private bool DiagnoseCorruptBlob(
        uint srcIdent,
        Exception exception,
        out RigSpec? rig)
    {
        rig = null;
        DiagnoseOnce(
            srcIdent,
            $"Setup DAT record 0x{srcIdent:X8} is malformed " +
            $"({exception.GetType().Name}: {exception.Message}); " +
            "activation skipped.");
        return false;
    }

    private void DiagnoseOnce(uint srcIdent, string msg)
    {
        if (_diagnosed.TryAdd(srcIdent, 0))
            _diagnostic(msg);
    }
}
