namespace MacAC.Mechanics.Kinetics;

public static class ContactExemption
{
    private const uint EtherealPs = 0x4u;
    private const uint IgnoreImpactsPs = 0x10u;

    public static bool ShouldSkip(uint markPhase, ActorImpactFlagSet markFlagSet, MoverState carrierPhase)
    {
        if ((markPhase & (EtherealPs | IgnoreImpactsPs)) == (EtherealPs | IgnoreImpactsPs))
            return true;

        bool markIsBeast = Has(markFlagSet, ActorImpactFlagSet.IsCreature);
        if (markIsBeast && (carrierPhase & (MoverState.IsViewer | MoverState.IgnoreCreatures)) != 0)
            return true;

        bool avatarDuo = (carrierPhase & MoverState.IsPlayer) != 0 && Has(markFlagSet, ActorImpactFlagSet.IsPlayer);
        if (!avatarDuo)
            return false;

        // Two players only collide when one is impenetrable or both share a PK flavour.
        return !(BothSet(carrierPhase, MoverState.IsPK, markFlagSet, ActorImpactFlagSet.IsPK)
            || BothSet(carrierPhase, MoverState.IsPKLite, markFlagSet, ActorImpactFlagSet.IsPKLite)
            || (carrierPhase & MoverState.IsImpenetrable) != 0
            || Has(markFlagSet, ActorImpactFlagSet.IsImpenetrable));
    }

    private static bool Has(ActorImpactFlagSet flagSet, ActorImpactFlagSet bit) => (flagSet & bit) != 0;

    private static bool BothSet(MoverState carrier, MoverState carrierBit, ActorImpactFlagSet mark, ActorImpactFlagSet markBit) =>
        (carrier & carrierBit) != 0 && (mark & markBit) != 0;
}
