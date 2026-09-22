using MacAC.Dat;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class ProxyRegistry
{

    public bool TryFetchRetailCellArray(
        uint actorIdent,
        out IReadOnlyList<uint> chambers)
    {
        if (_canonChambersByHolder.TryGetValue(actorIdent, out List<uint>? roster))
        {
            chambers = roster;
            return true;
        }
        chambers = Array.Empty<uint>();
        return false;
    }

    public IReadOnlyList<CanonPartRow> FetchCanonPieceListingsInChamber(uint chamberIdent)
    {
        return _canonRanksByChamber.TryGetValue(chamberIdent, out List<CanonPartRow>? listings)
            ? listings
            : Array.Empty<CanonPartRow>();
    }

    public CanonCellSetRoute FetchCanonChamberArrCourse(uint actorIdent)
    {
        return _canonCoursesByHolder.TryGetValue(actorIdent, out CanonCellSetRoute course)
            ? course
            : CanonCellSetRoute.None;
    }

    private (IReadOnlyList<uint> Cells, CanonCellSetRoute Route) CourseCanonChambers(
        uint seedChamberIdent,
        Vector3 realmSpot,
        Quaternion realmRot,
        uint phase,
        IReadOnlyList<ProxyShape> impactForms,
        IReadOnlyList<ProxyShape> pieceArr,
        bool isStatic)
    {
        bool hasCylsphere = false;
        for (int idx = 0; idx < impactForms.Count; ++idx)
        {
            if (impactForms[idx].ImpactKind == ProxyContactType.Cylinder)
            {
                hasCylsphere = true;
                break;
            }
        }
        bool cylsphereCourse = (phase & 0x10000u) is 0u && hasCylsphere;

        if (cylsphereCourse)
        {
            var cylOrbs =
                FloodOrbsFor(realmSpot, realmRot, impactForms);
            var chambers = CellHop.AssembleShadeChamberSet(
                StashForFlood, seedChamberIdent, cylOrbs, cylOrbs.Count, isStatic);
            return (chambers, CanonCellSetRoute.Cylsphere);
        }
        else
        {
            var bboxes =
                FloodBboxesFor(realmSpot, realmRot, pieceArr);
            var orbs =
                BspOrbsFor(realmSpot, realmRot, pieceArr);
            var chambers = CellHop.AssembleShadeChamberSetFromPieces(
                StashForFlood, seedChamberIdent, bboxes, orbs, isStatic);
            return (chambers, CanonCellSetRoute.BoundingBox);
        }
    }

    private void BroadcastCanonChambers(
        uint actorIdent,
        IReadOnlyList<uint> chamberArr,
        CanonCellSetRoute course,
        IReadOnlyList<ProxyShape> pieceArr)
    {
        if (_ancestorOf.ContainsKey(actorIdent))
        {
            if (pieceArr.Count is not 0)
                _descendantForms[actorIdent] = pieceArr;
            _canonCoursesByHolder[actorIdent] = course;
            BroadcastDescendantListings(actorIdent);
            return;
        }
        if (_canonChambersByHolder.TryGetValue(actorIdent, out List<uint>? earlierChambers))
        {
            DiscardCanonRanks(actorIdent, earlierChambers);
            _canonChambersByHolder.Remove(actorIdent);
        }
        _canonCoursesByHolder[actorIdent] = course;
        if (chamberArr.Count is 0)
        {
            RepublishAffixedDescendants(actorIdent);
            return;
        }

        List<uint> sequencedChambers = new List<uint>(chamberArr.Count);
        for (int idx = 0; idx < chamberArr.Count; ++idx)
            sequencedChambers.Add(chamberArr[idx]);
        _canonChambersByHolder[actorIdent] = sequencedChambers;
        BroadcastCanonRanks(actorIdent, sequencedChambers, pieceArr);
        RepublishAffixedDescendants(actorIdent);
    }

    private void BroadcastCanonProduct(
        uint actorIdent,
        IReadOnlyList<uint> preciseChambers)
    {
        if (!_canonPiecesByHolder.TryGetValue(
                actorIdent,
                out IReadOnlyList<ProxyShape>? pieceArr)
            || pieceArr.Count is 0)

            return;
        CanonCellSetRoute course = _canonCoursesByHolder.TryGetValue(
                actorIdent,
                out CanonCellSetRoute extantCourse)
            ? extantCourse
            : CanonCellSetRoute.None;
        BroadcastCanonChambers(actorIdent, preciseChambers, course, pieceArr);
    }

    private void DiscardCanonChambers(uint actorIdent)
    {
        if (_canonChambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers))
        {
            DiscardCanonRanks(actorIdent, chambers);
            _canonChambersByHolder.Remove(actorIdent);
        }
        _canonCoursesByHolder.Remove(actorIdent);
        _canonPiecesByHolder.Remove(actorIdent);
    }

    private void DiscardCanonRanks(
        uint actorIdent,
        IReadOnlyList<uint> chamberIdents)
    {
        for (int idx = 0; idx < chamberIdents.Count; ++idx)
        {
            if (_canonRanksByChamber.TryGetValue(
                    chamberIdents[idx],
                    out List<CanonPartRow>? listings))
            {
                DropHolderPieceRanks(listings, actorIdent);
                if (listings.Count is 0)
                    _canonRanksByChamber.Remove(chamberIdents[idx]);
            }
        }
    }

    private void DropHolderPieceRanks(
        List<CanonPartRow> listings,
        uint actorIdent)
    {
        for (int ordinal = listings.Count - 1; ordinal >= 0; --ordinal)
        {
            if (listings[ordinal].EntityId == actorIdent)
            {
                listings.RemoveAt(ordinal);
                StampChamber(listings);
            }
        }
    }

    private static ProxyEntry[] CollectHolderRanks(
        List<ProxyEntry> listings,
        uint actorIdent)
    {
        int tally = 0;
        for (int ordinal = 0; ordinal < listings.Count; ++ordinal)
        {
            if (listings[ordinal].EntityId == actorIdent)
                ++tally;
        }
        if (tally is 0)
            return Array.Empty<ProxyEntry>();
        ProxyEntry[] ranks = new ProxyEntry[tally];
        int written = 0;
        for (int ordinal = 0; ordinal < listings.Count; ++ordinal)
        {
            if (listings[ordinal].EntityId == actorIdent)
                ranks[written++] = listings[ordinal];
        }
        return ranks;
    }

    private static CanonPartRow[] CollectHolderPieceRanks(
        List<CanonPartRow> listings,
        uint actorIdent)
    {
        int tally = 0;
        for (int ordinal = 0; ordinal < listings.Count; ++ordinal)
        {
            if (listings[ordinal].EntityId == actorIdent)
                ++tally;
        }
        if (tally is 0)
            return Array.Empty<CanonPartRow>();
        CanonPartRow[] ranks = new CanonPartRow[tally];
        int written = 0;
        for (int ordinal = 0; ordinal < listings.Count; ++ordinal)
        {
            if (listings[ordinal].EntityId == actorIdent)
                ranks[written++] = listings[ordinal];
        }
        return ranks;
    }

    private void BroadcastCanonRanks(
        uint actorIdent,
        IReadOnlyList<uint> sequencedChambers,
        IReadOnlyList<ProxyShape> pieceArr)
    {
        bool clipPlanesNeeded = sequencedChambers.Count > 1;
        for (int chamberOrdinal = 0; chamberOrdinal < sequencedChambers.Count; ++chamberOrdinal)
        {
            uint chamberIdent = sequencedChambers[chamberOrdinal];
            if (!_canonRanksByChamber.TryGetValue(
                    chamberIdent,
                    out List<CanonPartRow>? listings))
            {
                listings = new List<CanonPartRow>();
                _canonRanksByChamber[chamberIdent] = listings;
            }
            StampChamber(listings);
            for (int pieceOrdinal = 0; pieceOrdinal < pieceArr.Count; ++pieceOrdinal)
            {
                listings.Add(new CanonPartRow(
                    actorIdent,
                    pieceOrdinal,
                    pieceArr[pieceOrdinal].GfxObjId,
                    chamberIdent,
                    clipPlanesNeeded));
            }
        }
    }

    private static List<Orb> FloodOrbsFor(
        Vector3 actorRealmSpot,
        Quaternion actorRealmRot,
        IReadOnlyList<ProxyShape> forms)
    {
        const int CanonOrbCap = 10;

        var orbs = new List<Orb>();
        bool anyCyl = false;
        foreach (var shape in forms)
        {
            if (shape.ImpactKind == ProxyContactType.Cylinder) anyCyl = true;
        }

        ProxyContactType sole =
            anyCyl ? ProxyContactType.Cylinder : ProxyContactType.Sphere;

        int cap = sole == ProxyContactType.Cylinder ? CanonOrbCap : int.MaxValue;

        foreach (var shape in forms)
        {
            if (shape.ImpactKind != sole)
                continue;
            if (orbs.Count >= cap)
                break;

            Vector3 pieceRealmSpot = actorRealmSpot + Vector3.Transform(shape.OwnPlace, actorRealmRot);
            Quaternion pieceRealmRot = actorRealmRot * shape.OwnSpin;
            Vector3 realm = pieceRealmSpot + Vector3.Transform(shape.LimitsMiddle, pieceRealmRot);
            orbs.Add(new Orb
            {
                Center = realm,
                Radius = shape.Radius,
            });
        }

        return orbs;
    }

    private static List<ProxyPartBox> FloodBboxesFor(
        Vector3 actorRealmSpot,
        Quaternion actorRealmRot,
        IReadOnlyList<ProxyShape> forms)
    {
        List<ProxyPartBox> bboxes = new List<ProxyPartBox>(forms.Count);
        foreach (var shape in forms)
        {
            if (shape.ImpactKind != ProxyContactType.BSP)
                continue;
            bboxes.Add(ProxyPartBox.FromForm(shape, actorRealmSpot, actorRealmRot));
        }
        return bboxes;
    }

    private static List<Orb> BspOrbsFor(
        Vector3 actorRealmSpot,
        Quaternion actorRealmRot,
        IReadOnlyList<ProxyShape> forms)
    {
        var orbs = new List<Orb>(forms.Count);
        foreach (var shape in forms)
        {
            if (shape.ImpactKind != ProxyContactType.BSP)
                continue;
            Vector3 pieceRealmSpot = actorRealmSpot + Vector3.Transform(shape.OwnPlace, actorRealmRot);
            Quaternion pieceRealmRot = actorRealmRot * shape.OwnSpin;
            orbs.Add(new Orb
            {
                Center = pieceRealmSpot + Vector3.Transform(shape.LimitsMiddle, pieceRealmRot),
                Radius = shape.Radius,
            });
        }
        return orbs;
    }

    private static uint ExteriorSeedFor(
        Vector3 realmSpot, float realmShiftX, float realmShiftY, uint lbIdent)
    {
        if (lbIdent is 0u) return 0u;
        float ownX = realmSpot.X - realmShiftX;
        float ownY = realmSpot.Y - realmShiftY;
        int cx = (int)System.Math.Clamp(ownX / 24f, 0f, 7f);
        int cy = (int)System.Math.Clamp(ownY / 24f, 0f, 7f);
        uint lbStem = lbIdent & 0xFFFF0000u;
        return lbStem | (uint)(cx * 8 + cy + 1);
    }

    private void FileRank(ProxyEntry listing, uint chamberIdent)
    {
        if (!_ranksByChamber.TryGetValue(chamberIdent, out var roster))
        {
            roster = new List<ProxyEntry>();
            _ranksByChamber[chamberIdent] = roster;
        }
        roster.Add(listing);
    }

    private static void DropHolderRanks(
        List<ProxyEntry> listings,
        uint actorIdent)
    {
        for (int ordinal = listings.Count - 1; ordinal >= 0; --ordinal)
        {
            if (listings[ordinal].EntityId == actorIdent)
                listings.RemoveAt(ordinal);
        }
    }

    private void RewriteLocusRanks(
        uint actorIdent,
        EnrolmentRecord enrollment,
        Vector3 realmLocus,
        Quaternion realmSpin,
        uint seedChamberIdent)
    {
        if ((_chambersByHolder.TryGetValue(
                 actorIdent,
                 out List<uint>? keptChambers)
             || _dormantHolderChambers.TryGetValue(
                 actorIdent,
                 out keptChambers))
            && keptChambers.Count is not 0)
        {
            SwapLocusRanks(
                actorIdent,
                enrollment,
                realmLocus,
                realmSpin,
                seedChamberIdent,
                keptChambers);
            return;
        }

        if (!_chambersByHolder.ContainsKey(actorIdent)
            && !_dormantHolderChambers.ContainsKey(actorIdent)
            && seedChamberIdent is not 0u
            && _canonPiecesByHolder.TryGetValue(
                actorIdent,
                out IReadOnlyList<ProxyShape>? rasterizePieceArr)
            && rasterizePieceArr.Count is not 0)
        {
            _dormantHolders.Remove(actorIdent);
            _enrolments[actorIdent] = enrollment with
            {
                SeedCellId = seedChamberIdent,
                EntityWorldPos = realmLocus,
                EntityWorldRot = realmSpin,
            };
            _singleChamberTemp[0] = seedChamberIdent;
            BroadcastCanonProduct(actorIdent, _singleChamberTemp);
            BumpHolderVer(actorIdent);
            return;
        }

        _enrolments[actorIdent] = enrollment with
        {
            SeedCellId = seedChamberIdent,
            EntityWorldPos = realmLocus,
            EntityWorldRot = realmSpin,
        };
        BumpHolderVer(actorIdent);
    }

    private readonly uint[] _singleChamberTemp = new uint[1];

    private void SwapLocusRanks(
        uint actorIdent,
        EnrolmentRecord enrollment,
        Vector3 realmLocus,
        Quaternion realmSpin,
        uint seedChamberIdent,
        IReadOnlyList<uint> chamberIdents)
    {
        if (_chambersByHolder.TryGetValue(
                actorIdent,
                out List<uint>? earlierChambers))
        {
            for (int ordinal = 0; ordinal < earlierChambers.Count; ++ordinal)
            {
                if (_ranksByChamber.TryGetValue(
                        earlierChambers[ordinal],
                        out List<ProxyEntry>? listings))

                    DropHolderRanks(listings, actorIdent);
            }
        }

        _dormantHolders.Remove(actorIdent);
        _dormantHolderChambers.Remove(actorIdent);
        _enrolments[actorIdent] = enrollment with
        {
            SeedCellId = seedChamberIdent,
            EntityWorldPos = realmLocus,
            EntityWorldRot = realmSpin,
        };

        List<uint> preciseChambers = new List<uint>(chamberIdents.Count);
        for (int ordinal = 0; ordinal < chamberIdents.Count; ++ordinal)
        {
            uint chamberIdent = chamberIdents[ordinal];
            if (chamberIdent is 0u || preciseChambers.Contains(chamberIdent))
                continue;
            preciseChambers.Add(chamberIdent);
        }

        IReadOnlyList<ProxyShape>? forms = null;
        bool isMultiPieceRelay = enrollment.IsMultiPart
            && _formsByHolder.TryGetValue(actorIdent, out forms);
        if (isMultiPieceRelay)
        {
            foreach (ProxyShape form in forms!)
            {
                Vector3 pieceRealmLocus = realmLocus
                    + Vector3.Transform(form.OwnPlace, realmSpin);
                Quaternion pieceRealmSpin = realmSpin
                    * form.OwnSpin;
                ProxyEntry listing = new ProxyEntry(
                    actorIdent,
                    form.GfxObjId,
                    pieceRealmLocus,
                    pieceRealmSpin,
                    form.Radius,
                    form.ImpactKind,
                    form.CylHeight,
                    form.Scale,
                    enrollment.State,
                    enrollment.Flags,
                    form.OwnPlace,
                    form.OwnSpin);
                for (int ordinal = 0; ordinal < preciseChambers.Count; ++ordinal)
                    FileRank(listing, preciseChambers[ordinal]);
            }
        }
        else
        {
            ProxyEntry listing = new ProxyEntry(
                actorIdent,
                enrollment.GfxObjId,
                realmLocus,
                realmSpin,
                enrollment.Radius,
                enrollment.CollisionType,
                enrollment.CylHeight,
                enrollment.Scale,
                enrollment.State,
                enrollment.Flags);
            for (int ordinal = 0; ordinal < preciseChambers.Count; ++ordinal)
                FileRank(listing, preciseChambers[ordinal]);
        }

        bool wroteImpactListings = !isMultiPieceRelay || forms!.Count is not 0;
        if (preciseChambers.Count is 0 || !wroteImpactListings)
            _chambersByHolder.Remove(actorIdent);
        else
            _chambersByHolder[actorIdent] = preciseChambers;
        if (_withdrawnByHolder.TryGetValue(
                actorIdent,
                out HashSet<uint>? withdrawn))
        {
            for (int ordinal = 0; ordinal < preciseChambers.Count; ++ordinal)
                withdrawn.Remove(preciseChambers[ordinal] & 0xFFFF0000u);
            if (withdrawn.Count is 0)
                _withdrawnByHolder.Remove(actorIdent);
        }
        BroadcastCanonProduct(actorIdent, preciseChambers);
        BumpHolderVer(actorIdent);
    }
}
