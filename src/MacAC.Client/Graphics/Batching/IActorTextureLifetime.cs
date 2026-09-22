namespace MacAC.Client.Graphics.Batching;

public interface IActorTextureLifetime
{
    void FreeHolder(uint ownActorIdent);
}
