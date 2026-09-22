using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Sim;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{
    public bool IsAvailable
    {
        get
        {
            SimCore? core;
            lock (_latch)
            {
                if (_destroyed || _toon is null || _casting is null)
                    return false;
                core = _runtime;
            }
            return core is not null
                && core.Lifecycle.State == SimLifespanPhase.InWorld;
        }
    }

    bool INetworkControls.IsAvailable
    {
        get
        {
            lock (_latch)
                return !_destroyed;
        }
    }

    bool ILoginControls.IsAvailable
    {
        get
        {
            lock (_latch)
                return !_destroyed && _runtime is not null;
        }
    }

    public bool IsInWorld => IsAvailable;

    public bool IsRecognized(uint arcanumIdent)
    {
        Grimoire? grimoire;
        lock (_latch)
            grimoire = _grimoire;
        return grimoire?.LearnedArcana.Contains(arcanumIdent) == true;
    }

    bool IProjectileControls.IsAvailable
    {
        get
        {
            lock (_latch)
                return !_destroyed && _missileKinetics is not null && IsAvailable;
        }
    }

    bool IWorldObjectControls.IsAvailable => IsAvailable;

    public bool IsCasting
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core is not null
                && core.SatchelHolder.Transactions.OccupiedCount > 0;
        }
    }

    bool IEquipmentControls.IsAvailable
    {
        get
        {
            lock (_latch)
                return !_destroyed && _wield is not null && IsAvailable;
        }
    }

    bool IEquipmentControls.IsBusy
    {
        get
        {
            Func<bool>? occupied;
            lock (_latch)
                occupied = _equipmentOccupied;
            return occupied?.Invoke() == true;
        }
    }

    bool IItemControls.IsAvailable
    {
        get
        {
            lock (_latch)
                return !_destroyed && _useGear is not null
                    && _enactGear is not null && IsAvailable;
        }
    }

    bool IItemControls.IsBusy
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core is not null
                && !core.SatchelHolder.Transactions.CanCommenceReq;
        }
    }

    private static bool IsAvatarPossessed(
        ClientThing gear,
        uint avatarIdent,
        ClientThingChart objects)
    {
        if (gear.WielderIdent == avatarIdent || gear.VesselTag == avatarIdent)
            return true;
        uint ancestorIdent = gear.VesselTag;
        for (int zDepth = 0; ancestorIdent is not 0u && zDepth < 4; ++zDepth)
        {
            var ancestor = objects.Get(ancestorIdent);
            if (ancestor is null)
                return false;
            if (ancestor.WielderIdent == avatarIdent || ancestor.VesselTag == avatarIdent)
                return true;
            ancestorIdent = ancestor.VesselTag;
        }
        return false;
    }

    bool ILootControls.IsAvailable
    {
        get
        {
            lock (_latch)
                return !_destroyed && _useGear is not null
                    && _liftGear is not null
                    && _recognizeGear is not null
                    && IsAvailable;
        }
    }

    bool ILootControls.IsBusy
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core is not null
                && !core.SatchelHolder.Transactions.CanCommenceReq;
        }
    }

    public bool IsInFellowship
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.Fellowship.Snapshot.IsInFellowship == true;
        }
    }

    public bool IsOpen
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.Fellowship.Snapshot.IsOpen == true;
        }
    }

    public bool IsBolted
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.Fellowship.Snapshot.Locked == true;
        }
    }
}
