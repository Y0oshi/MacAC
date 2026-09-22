using System.Numerics;

namespace MacAC.Mechanics.Effects;

/// <summary>Where entities and their parts are right now, for attaching effects.</summary>
public interface IEffectPoseSource
{
    bool TryFetchTrunkPosture(uint ownActorIdent, out Matrix4x4 trunkRealm);

    bool TryFetchPiecePosture(uint ownActorIdent, int pieceOrdinal, out Matrix4x4 pieceOwn);
}

public interface IActorFxPostureEditOrigin
{
    event Action<uint>? EffectPoseChanged;
}

public interface IActorFxChamberOrigin
{
    bool TryFetchChamberIdent(uint ownActorIdent, out uint chamberIdent);
}
