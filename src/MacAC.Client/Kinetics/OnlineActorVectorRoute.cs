namespace MacAC.Client.Kinetics;

internal enum OnlineActorVectorRoute
{
    Projectile,
    CanonicalBody,
    OrdinaryRemote,
}

internal static class OnlineActorVectorRouter
{
    internal static OnlineActorVectorRoute Course(
        Func<bool> tryMissile,
        Func<bool> tryCanonCorpus,
        Action enactPlainDistant)
    {
        ArgumentNullException.ThrowIfNull(tryMissile);
        ArgumentNullException.ThrowIfNull(tryCanonCorpus);
        ArgumentNullException.ThrowIfNull(enactPlainDistant);

        if (tryMissile())
            return OnlineActorVectorRoute.Projectile;
        if (tryCanonCorpus())
            return OnlineActorVectorRoute.CanonicalBody;

        enactPlainDistant();
        return OnlineActorVectorRoute.OrdinaryRemote;
    }
}
