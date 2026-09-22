using MacAC.Assets;
using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim;
using MacAC.Sim.Actors;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{
    public bool TryFetchAptitude(uint aptitudeIdent, out SkillFacts aptitude)
    {
        SimToonLedger? toon;
        IReadOnlyDictionary<uint, string> labels;
        IReadOnlyDictionary<uint, uint> glyphs;
        lock (_latch)
        {
            toon = _toon;
            labels = _aptitudeLabels;
            glyphs = _aptitudeGlyphs;
        }
        if (toon is not null)
        {
            string label = labels.TryGetValue(aptitudeIdent, out string? num) ? num : string.Empty;
            uint glyphIdent = glyphs.TryGetValue(aptitudeIdent, out uint glyph) ? glyph : 0u;
            return TryProjectAptitude(toon, aptitudeIdent, label, glyphIdent, out aptitude);
        }
        aptitude = default;
        return false;
    }

    public bool TryGet(uint arcanumIdent, out SpellFacts details)
    {
        Grimoire? grimoire;
        lock (_latch)
            grimoire = _grimoire;
        if (grimoire is not null
            && grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta meta))
        {
            details = Project(meta);
            return true;
        }
        details = default;
        return false;
    }

    public bool TryFetchModule(uint moduleIdent, out SpellComponentFacts details)
    {
        ArcanaCatalog registry;
        lock (_latch)
            registry = _magicRegistry;
        if (registry.TryFetchModuleByArcanumModuleIdent(
                moduleIdent,
                out SpellComponentCard descriptor))
        {
            details = new SpellComponentFacts(
                descriptor.ArcanumModuleIdent,
                descriptor.WeenieClassId,
                descriptor.Name,
                descriptor.BurnRate,
                descriptor.GestureIdent,
                descriptor.GesturePace,
                descriptor.IconId,
                descriptor.Category,
                descriptor.Type,
                descriptor.Word);
            return true;
        }
        details = default;
        return false;
    }

    public bool TryFetchObject(uint objectIdent, out NavigationEntry val)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable || objectIdent is 0u)
        {
            val = default;
            return false;
        }

        var travel = core.Movement.Snapshot;
        if (objectIdent == core.AvatarIdentity.ServerGuid)
        {
            val = new NavigationEntry(
                objectIdent,
                core.SatchelHolder.Objects.Get(objectIdent)?.Name
                    ?? string.Empty,
                ProjectNavigationLocus(travel.Position));
            return travel.HasController;
        }

        if (!core.EntityObjects.Entities.TryFetchEngaged(
                objectIdent,
                out SimActorRecord capture))
        {
            val = default;
            return false;
        }

        Locus? locus = capture.KineticBody?.CellPosition
            ?? TranslateLocus(capture.Snapshot.Position);
        if (locus is not { } latest)
        {
            val = default;
            return false;
        }
        val = new NavigationEntry(
            objectIdent,
            core.SatchelHolder.Objects.Get(objectIdent)?.Name
                ?? capture.Snapshot.Name
                ?? $"0x{objectIdent:X8}",
            ProjectNavigationLocus(latest));
        val = EnrichNavigationObject(
            val,
            core.SatchelHolder.Objects.Get(objectIdent));
        return true;
    }

    bool IWorldObjectControls.TryGet(
        uint objectIdent,
        out WorldObjectEntry val)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable || objectIdent is 0u)
        {
            val = default;
            return false;
        }

        core.EntityObjects.Entities.TryFetchEngaged(
            objectIdent,
            out SimActorRecord? capture);
        var gear = core.SatchelHolder.Objects.Get(objectIdent);
        if (capture is null && gear is null)
        {
            val = default;
            return false;
        }
        val = ProjectWorldObject(
            core,
            capture,
            gear,
            core.AvatarIdentity.ServerGuid);
        return true;
    }

    private static bool TryFetchPossessed(
        ClientThingChart objects,
        uint avatarIdent,
        uint objectIdent,
        out ClientThing? gear)
    {
        gear = objectIdent is 0u ? null : objects.Get(objectIdent);
        return gear is not null && IsAvatarPossessed(gear, avatarIdent, objects);
    }
}
