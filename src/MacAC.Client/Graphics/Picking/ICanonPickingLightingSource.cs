namespace MacAC.Client.Graphics.Picking;

internal interface ICanonPickingLightingSource
{
    void PulseIllumination();

    bool TryFetchIllumination(
        uint srvOid,
        uint ownActorIdent,
        out CanonPickingLighting illumination);
}
