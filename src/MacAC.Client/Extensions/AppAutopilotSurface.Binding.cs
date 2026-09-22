using MacAC.Assets;
using MacAC.Client.SimBridge;
using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Genesis;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{
    // Bind the surface to the runtime's gameplay owners
    public void Bind(
        SimCore core, SimToonLedger toon, SimArcanaCastLedger casting)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(toon);
        ArgumentNullException.ThrowIfNull(casting);

        Grimoire grimoire = toon.Spellbook;
        lock (_latch)
        {
            if (_destroyed)
                return;
            UnfastenBolted();
            _runtime = core;
            _communication = core.CommunicationHolder;
            _communicationSubscription =
                core.CommunicationHolder.Events.Subscribe(this);
            _toon = toon;
            _casting = casting;
            _grimoire = grimoire;
            grimoire.SpellbookChanged += OnGrimoireAltered;
            grimoire.EnchantmentsChanged += OnEnchantmentsAltered;
            core.SatchelHolder.Transactions.RequestCompleted +=
                OnSatchelReqFinished;
            core.SatchelHolder.Transactions.RequestFailed +=
                OnSatchelReqFailed;
        }

        ReassembleGrimoire();
        ReassembleEnchantments();
    }

    public void AttachAptitudeLabels(IReadOnlyDictionary<uint, string> aptitudeLabels)
    {
        ArgumentNullException.ThrowIfNull(aptitudeLabels);
        lock (_latch)
            _aptitudeLabels = aptitudeLabels;
    }

    public void AttachAptitudeGlyphs(IReadOnlyDictionary<uint, uint> aptitudeGlyphs)
    {
        ArgumentNullException.ThrowIfNull(aptitudeGlyphs);
        lock (_latch)
            _aptitudeGlyphs = aptitudeGlyphs;
    }

    public void AttachMagicRegistry(ArcanaCatalog registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        lock (_latch)
            _magicRegistry = registry;
    }

    public void AttachSessDirectives(CurrentGameEngineBridge directives)
    {
        ArgumentNullException.ThrowIfNull(directives);
        lock (_latch)
            _sessDirectives = directives;
    }

    public void AttachSpeciesLabelLocator(Func<int, string> locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        lock (_latch)
            _speciesLabel = locator;
    }

    public void AttachSwatchTintLocator(IGenesisPaletteColorSource locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        lock (_latch)
            _swatchTints = locator;
    }

    public void AttachEquipment(
        Func<uint, uint, bool> wield,
        Func<bool> isOccupied)
    {
        ArgumentNullException.ThrowIfNull(wield);
        ArgumentNullException.ThrowIfNull(isOccupied);
        lock (_latch)
        {
            _wield = wield;
            _equipmentOccupied = isOccupied;
        }
    }

    public void AttachGearList(
        Func<uint, bool> useGear,
        Func<uint, uint, bool> enactGear,
        Func<uint, uint, uint, int, bool> relocateGear,
        Func<uint, uint, uint, bool> combineGearList,
        Func<uint, uint, bool> discardGear,
        Func<uint, uint, uint, bool> handGear,
        Func<uint, bool, bool> liftGear,
        Func<uint, bool> recognizeGear,
        Func<uint, IReadOnlyList<uint>, bool>? salvageGearList = null,
        Func<uint, uint, int, bool>? vendGear = null)
    {
        ArgumentNullException.ThrowIfNull(useGear);
        ArgumentNullException.ThrowIfNull(enactGear);
        ArgumentNullException.ThrowIfNull(relocateGear);
        ArgumentNullException.ThrowIfNull(combineGearList);
        ArgumentNullException.ThrowIfNull(discardGear);
        ArgumentNullException.ThrowIfNull(handGear);
        ArgumentNullException.ThrowIfNull(liftGear);
        ArgumentNullException.ThrowIfNull(recognizeGear);
        lock (_latch)
        {
            _useGear = useGear;
            _enactGear = enactGear;
            _relocateGear = relocateGear;
            _combineGearList = combineGearList;
            _discardGear = discardGear;
            _handGear = handGear;
            _liftGear = liftGear;
            _recognizeGear = recognizeGear;
            _salvageGearList = salvageGearList;
            _vendGear = vendGear;
        }
    }

    public void AttachGhostDeletion(Func<uint, bool> dismissGhost)
    {
        ArgumentNullException.ThrowIfNull(dismissGhost);
        lock (_latch)
            _dismissGhost = dismissGhost;
    }

    public void AttachMissileImpact(KineticEngine kinetics)
    {
        ArgumentNullException.ThrowIfNull(kinetics);
        lock (_latch)
            _missileKinetics = kinetics;
    }

    public void AttachPickActs(
        Func<SelectionVerb, bool> perform)
    {
        ArgumentNullException.ThrowIfNull(perform);
        lock (_latch)
            _pickAct = perform;
    }
}
