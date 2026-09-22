namespace MacAC.Client.Graphics;

internal interface IPlayRasterizeAssetLifespan
{
    LandTileset ObtainLandTileset(Func<LandTileset> maker);
}

// Sole lifetime owner for render resources that are borrowed by, but not owned by, their renderers
internal sealed class PlayRasterizeAssetLifespan : IPlayRasterizeAssetLifespan
{
    private readonly PossessedAssetSocket<LandTileset> _landTileset = new();

    public LandTileset ObtainLandTileset(Func<LandTileset> maker) =>
        _landTileset.Acquire(maker);

    public void FreeLandTileset() => _landTileset.Release();
}
