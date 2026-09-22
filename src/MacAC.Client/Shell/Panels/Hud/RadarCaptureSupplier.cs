using System.Numerics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Shell;
using MacAC.Wire;

namespace MacAC.Client.Shell.Panels;

public sealed class RadarCaptureSupplier(
    ClientThingChart objects,
    IOnlineActorRadarSource liveEntities,
    Func<IReadOnlyDictionary<uint, RealmSession.MoverSpawn>> spawns,
    Func<uint> playerGuid,
    Func<float> playerYawRadians,
    Func<uint> playerCellId,
    Func<uint?> selectedGuid,
    Func<bool> coordinatesOnRadar,
    Func<bool> uiLocked,
    Func<uint, RadarRelationFacets>? relationshipFor = null,
    Func<IOnlineActorSpatialProbe?>? spatialAsk = null)
{
    private static readonly Vector2 ProductionMiddle = new(60f, 60f);

    private readonly ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private readonly IOnlineActorRadarSource _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly Func<IReadOnlyDictionary<uint, RealmSession.MoverSpawn>> _spawns = spawns ?? throw new ArgumentNullException(nameof(spawns));
    private readonly Func<uint> _avatarOid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
    private readonly Func<float> _avatarYawRadians = playerYawRadians ?? throw new ArgumentNullException(nameof(playerYawRadians));
    private readonly Func<uint> _avatarChamberIdent = playerCellId ?? throw new ArgumentNullException(nameof(playerCellId));
    private readonly Func<uint?> _chosenOid = selectedGuid ?? throw new ArgumentNullException(nameof(selectedGuid));
    private readonly Func<bool> _coordinatesOnRadar = coordinatesOnRadar ?? throw new ArgumentNullException(nameof(coordinatesOnRadar));
    private readonly Func<bool> _widgetBolted = uiLocked ?? throw new ArgumentNullException(nameof(uiLocked));
    private readonly Func<uint, RadarRelationFacets>? _relationshipFor = relationshipFor;
    private readonly Func<IOnlineActorSpatialProbe?>? _spatialAsk = spatialAsk;
    private readonly List<KeyValuePair<uint, RealmActor>> _contenderTemp = [];

    public WidgetRadarCapture AssembleCapture()
    {
        var spawns = _spawns();

        bool widgetBolted = _widgetBolted();
        uint avatarOid = _avatarOid();
        if (avatarOid is 0u
            || !_onlineActors.TryFetchMaterialized(
                avatarOid,
                out RealmActor avatarActor))
            return WidgetRadarCapture.Empty with { UiLocked = widgetBolted };

        float bearing = ApproachMath.BearingFromYaw(_avatarYawRadians());
        uint avatarChamberIdent = _avatarChamberIdent();
        bool isBeyond = RadarCoords.TryFromChamber(avatarChamberIdent, out var avatarCoordinates);
        string? coordinates = _coordinatesOnRadar() && isBeyond
            ? avatarCoordinates.CombinedPhrase
            : null;

        uint avatarPwd = spawns.TryGetValue(avatarOid, out var avatarSummon)
            ? avatarSummon.ObjectDescriptionFlags ?? 0u
            : 0u;
        RadarObjectFacets avatarTraits = RadarObjectFacets.FromPublicWeenieBlurb(
            (uint)(_objects.Get(avatarOid)?.Type ?? GearKind.None), avatarPwd);
        float span = CanonRadar.FetchSpanMeters(isBeyond);

        _contenderTemp.Clear();
        if (_spatialAsk?.Invoke() is { } spatialAsk)
        {
            spatialAsk.DuplicateOnlineActorsNearbyLb(avatarChamberIdent, 1, _contenderTemp);
        }
        else
        {
            _onlineActors.DuplicateShownTo(_contenderTemp);
        }

        var blips = new List<WidgetRadarBlip>(Math.Min(_contenderTemp.Count, 64));
        foreach (var duo in _contenderTemp)
        {
            uint oid = duo.Key;
            if (oid == avatarOid)
                continue;

            RealmActor actor = duo.Value;
            if (!_onlineActors.TryFetchShown(oid, out RealmActor? shownActor)
                || !ReferenceEquals(actor, shownActor))

                continue;
            ClientThing? clientObject = _objects.Get(oid);
            spawns.TryGetValue(oid, out var summon);

            byte? rawBehavior = clientObject?.RadarBehavior ?? summon.RadarBehavior;
            if (rawBehavior is null
                || !CanonRadar.IsShowable((MechRadarBehavior)rawBehavior.Value, hasKineticsObject: true))

                continue;

            uint gearKind = (uint)(clientObject?.Type ?? GearKind.None);
            if (gearKind is 0u)
                gearKind = summon.ItemType ?? 0u;
            uint pwd = summon.ObjectDescriptionFlags ?? 0u;
            byte tintOverride = clientObject?.RadarBlipColor ?? summon.RadarBlipColor ?? 0;
            RadarObjectFacets traits = RadarObjectFacets.FromPublicWeenieBlurb(
                gearKind, pwd, tintOverride);

            var relationship = _relationshipFor?.Invoke(oid) ?? default;
            relationship = relationship with
            {
                PlayerIsPlayerKiller = avatarTraits.IsPlayerKiller,
                PlayerIsPkLite = avatarTraits.IsPkLite,
            };
            RadarBlipGlyph form = CanonRadar.FetchBlipForm(traits, relationship);
            if (form == RadarBlipGlyph.Undef)
                continue;

            Vector3 avatarSpace = ApproachMath.GlobalToOwnVec(
                avatarActor.Rotation,
                actor.Position - avatarActor.Position);
            if (!CanonRadar.TryProject(
                    avatarSpace,
                    ProductionMiddle,
                    RadarDriver.RadarPixelRadius,
                    span,
                    out var proj))

                continue;

            RadarBlipPalette.MechRgba tint = RadarBlipPalette.For(traits, relationship)
                .DimRgb(proj.RgbMultiplier);
            string? label = clientObject?.Name;
            if (string.IsNullOrEmpty(label))
                label = summon.Name ?? $"0x{oid:X8}";

            blips.Add(new WidgetRadarBlip(
                ObjectId: oid,
                Name: label,
                PixelX: proj.Pixel.X,
                PixelY: proj.Pixel.Y,
                Color: new Vector4(tint.Red, tint.Green, tint.Blue, tint.Alpha),
                Shape: form,
                Selected: _chosenOid() == oid));
        }

        return new WidgetRadarCapture(
            PlayerHeadingDegrees: bearing,
            Blips: blips,
            CoordinatesText: coordinates,
            BlankBlips: false,
            UiLocked: widgetBolted);
    }
}
