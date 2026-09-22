using MacAC.Assets.Pak;
using MacAC.Mechanics.Kinetics;
using Microsoft.Extensions.Logging;

namespace MacAC.Assets;

/// <summary>
/// Everything the game needs prepared, built from the installed dats as it is asked for: meshes
/// from the harvester, collision from the same builders <c>macac-bake</c> uses. No pak, so nothing
/// to prepare before first play and nothing that can be stale.
///
/// Collision sits behind a bounded LRU because the world asks for the same doorways and shells
/// repeatedly while streaming; meshes are cached by their owners further up.
/// </summary>
public sealed class LiveBakedAssetSource : IBakedAssetSource, IBakedContactSource
{
    private DatBakedAssetSource? _meshes;
    private DatBakedContactSource? _rawContact;
    private SharedBakedContactShelf? _contact;

    public LiveBakedAssetSource(IDatAccess datFiles, ILogger logger, int collisionShelfCap = SharedBakedContactShelf.DefaultCap)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            _meshes = new DatBakedAssetSource(datFiles, logger);
            _rawContact = new DatBakedContactSource(datFiles);
            _contact = new SharedBakedContactShelf(_rawContact, collisionShelfCap);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private DatBakedAssetSource Meshes => _meshes ?? throw new ObjectDisposedException(nameof(LiveBakedAssetSource));
    private SharedBakedContactShelf Contact => _contact ?? throw new ObjectDisposedException(nameof(LiveBakedAssetSource));

    public BakedAssetSourceStats Stats => Meshes.Stats;

    public BakedContactSourceStats ImpactStats => Contact.ImpactStats;

    public ShelfStats DecodedTextureStashStats => Meshes.DecodedTextureStashStats;

    /// <summary>Distinct cell shapes flattened so far; many rooms share one.</summary>
    public int ShellsFlattened => _rawContact?.ShellsFlattened ?? 0;

    public BakedAssetPresence Probe(PakAssetKind kind, uint srcFileIdent) => Meshes.Probe(kind, srcFileIdent);

    public BakedAssetRead Read(in BakedAssetRequest req, CancellationToken abortTicket = default) => Meshes.Read(req, abortTicket);

    public BakedAssetPresence InspectImpact(PakAssetKind kind, uint srcFileIdent) => Contact.InspectImpact(kind, srcFileIdent);

    public BakedContactRead<PackedGfxObjContactAsset> ScanGfxObjRefImpact(uint srcFileIdent, CancellationToken abortTicket = default) =>
        Contact.ScanGfxObjRefImpact(srcFileIdent, abortTicket);

    public BakedContactRead<PackedSetupContact> ReadSetupCollision(uint srcFileIdent, CancellationToken abortTicket = default) =>
        Contact.ReadSetupCollision(srcFileIdent, abortTicket);

    public BakedContactRead<PackedCellStructContactAsset> ScanChamberStructureImpact(uint srcFileIdent, CancellationToken abortTicket = default) =>
        Contact.ScanChamberStructureImpact(srcFileIdent, abortTicket);

    public BakedContactRead<PackedEnvCellTopology> ScanEnvironChamberWiring(uint srcFileIdent, CancellationToken abortTicket = default) =>
        Contact.ScanEnvironChamberWiring(srcFileIdent, abortTicket);

    public void Dispose()
    {
        _contact?.Dispose();
        _contact = null;
        _rawContact?.Dispose();
        _rawContact = null;
        _meshes?.Dispose();
        _meshes = null;
    }
}
