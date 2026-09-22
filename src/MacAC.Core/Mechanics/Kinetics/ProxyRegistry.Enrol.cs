using MacAC.Dat;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Registering, updating, suspending and withdrawing owners.</summary>
public sealed partial class ProxyRegistry
{
    public void Register(uint actorIdent, uint gfxObjRefIdent, Vector3 realmSpot, Quaternion spin,
                         float radius, float realmShiftX, float realmShiftY, uint lbIdent,
                         ProxyContactType impactKind = ProxyContactType.BSP,
                         float cylHeight = 0f, float scaling = 1.0f,
                         uint phase = 0u,
                         ActorImpactFlagSet flagSet = ActorImpactFlagSet.None,
                         uint seedChamberIdent = 0u,
                         bool isStatic = true,
                         bool broadcastAlteration = true,
                         IReadOnlyList<ProxyShape>? pieceArr = null)
    {
        uint seed = seedChamberIdent is not 0u
            ? seedChamberIdent
            : ExteriorSeedFor(realmSpot, realmShiftX, realmShiftY, lbIdent);
        if (seed is 0u) return;

        bool hasCanonPieceArr = pieceArr is not null && pieceArr.Count is not 0;

        IReadOnlyList<uint> chamberSet;
        var canonCourse = CanonCellSetRoute.None;
        if (hasCanonPieceArr)
        {
            IReadOnlyList<ProxyShape> impactForms =
                impactKind == ProxyContactType.Cylinder
                    ? new[]
                    {
                        ProxyShape.Cylinder(
                            gfxObjRefIdent, Vector3.Zero, Quaternion.Identity, scaling, radius, cylHeight),
                    }
                    : Array.Empty<ProxyShape>();
            (chamberSet, canonCourse) = CourseCanonChambers(
                seed, realmSpot, spin, phase, impactForms, pieceArr!, isStatic);
        }
        else
        {
            var orbs = new[]
            {
                new Orb { Center = realmSpot, Radius = radius },
            };
            chamberSet = CellHop.AssembleShadeChamberSet(
                StashForFlood, seed, orbs, orbs.Length, isStatic);
        }
        if (chamberSet.Count is 0) return;

        Withdraw(actorIdent, broadcastAlteration: false);

        ProxyEntry listing = new ProxyEntry(actorIdent, gfxObjRefIdent, realmSpot, spin, radius,
                                    impactKind, cylHeight, scaling, phase, flagSet);

        List<uint> chamberIdents = new List<uint>(chamberSet.Count);
        foreach (uint chamberIdent in chamberSet)
        {
            FileRank(listing, chamberIdent);
            chamberIdents.Add(chamberIdent);
        }

        _chambersByHolder[actorIdent] = chamberIdents;
        _enrolments[actorIdent] = new EnrolmentRecord(
            seed, realmSpot, spin, phase, flagSet, isStatic,
            IsMultiPart: false, gfxObjRefIdent, radius, impactKind, cylHeight, scaling);
        if (broadcastAlteration)
            BumpHolderVer(actorIdent);
        else
            ReindexHolderStems(actorIdent);

        if (hasCanonPieceArr)
        {
            _canonPiecesByHolder[actorIdent] = pieceArr!;
            BroadcastCanonChambers(actorIdent, chamberSet, canonCourse, pieceArr!);
        }
    }

    public void EnrollMultiPiece(
        uint actorIdent,
        Vector3 actorRealmSpot,
        Quaternion actorRealmRot,
        IReadOnlyList<ProxyShape> forms,
        uint phase,
        ActorImpactFlagSet flagSet,
        float realmShiftX, float realmShiftY, uint lbIdent,
        uint seedChamberIdent = 0u,
        bool isStatic = false,
        bool broadcastAlteration = true,
        IReadOnlyList<ProxyShape>? pieceArr = null)
    {
        if (forms.Count is 0)
        {
            if (pieceArr is { Count: > 0 })
            {
                EnrolRasterizeSole(
                    actorIdent, actorRealmSpot, actorRealmRot, phase, flagSet,
                    realmShiftX, realmShiftY, lbIdent, seedChamberIdent,
                    isStatic, broadcastAlteration, pieceArr);
            }
            else
            {
                Deregister(actorIdent);
            }
            return;
        }

        // Flood FIRST - keep-when-empty, see Register
        uint seed = seedChamberIdent is not 0u
            ? seedChamberIdent
            : ExteriorSeedFor(actorRealmSpot, realmShiftX, realmShiftY, lbIdent);
        if (seed is 0u) return;

        bool hasCanonPieceArr = pieceArr is not null && pieceArr.Count is not 0;

        IReadOnlyList<uint> chamberSet;
        var canonCourse = CanonCellSetRoute.None;
        if (hasCanonPieceArr)
        {
            (chamberSet, canonCourse) = CourseCanonChambers(
                seed, actorRealmSpot, actorRealmRot, phase, forms, pieceArr!, isStatic);
        }
        else
        {
            bool hasBsp = false;
            for (int idx = 0; idx < forms.Count; ++idx)
            {
                if (forms[idx].ImpactKind == ProxyContactType.BSP)
                {
                    hasBsp = true;
                    break;
                }
            }

            if (hasBsp)
            {
                List<ProxyPartBox> pieceBboxes = FloodBboxesFor(actorRealmSpot, actorRealmRot, forms);
                var pieceOrbs = BspOrbsFor(actorRealmSpot, actorRealmRot, forms);
                chamberSet = CellHop.AssembleShadeChamberSetFromPieces(
                    StashForFlood, seed, pieceBboxes, pieceOrbs, isStatic);
            }
            else
            {
                var floodOrbs = FloodOrbsFor(actorRealmSpot, actorRealmRot, forms);
                chamberSet = CellHop.AssembleShadeChamberSet(
                    StashForFlood, seed, floodOrbs, floodOrbs.Count, isStatic);
            }
        }
        if (chamberSet.Count is 0) return;

        Withdraw(actorIdent, broadcastAlteration: false);
        _formsByHolder[actorIdent] = forms;
        List<uint> allChambers = new List<uint>(chamberSet.Count);

        foreach (var form in forms)
        {
            Vector3 rotatedOwn = Vector3.Transform(form.OwnPlace, actorRealmRot);
            Vector3 pieceRealmSpot = actorRealmSpot + rotatedOwn;
            Quaternion pieceRealmRot = actorRealmRot * form.OwnSpin;

            ProxyEntry listing = new ProxyEntry(
                EntityId: actorIdent,
                GfxObjId: form.GfxObjId,
                Position: pieceRealmSpot,
                Rotation: pieceRealmRot,
                Radius: form.Radius,
                CollisionType: form.ImpactKind,
                CylHeight: form.CylHeight,
                Scale: form.Scale,
                State: phase,
                Flags: flagSet,
                LocalPosition: form.OwnPlace,
                LocalRotation: form.OwnSpin);

            foreach (uint chamberIdent in chamberSet)
                FileRank(listing, chamberIdent);
        }

        foreach (uint chamberIdent in chamberSet)
            allChambers.Add(chamberIdent);

        _chambersByHolder[actorIdent] = allChambers;
        _enrolments[actorIdent] = new EnrolmentRecord(
            seed, actorRealmSpot, actorRealmRot, phase, flagSet, isStatic,
            IsMultiPart: true, GfxObjId: 0u, Radius: 0f,
            CollisionType: ProxyContactType.BSP, CylHeight: 0f, Scale: 1f);
        if (broadcastAlteration)
            BumpHolderVer(actorIdent);
        else
            ReindexHolderStems(actorIdent);

        if (hasCanonPieceArr)
        {
            _canonPiecesByHolder[actorIdent] = pieceArr!;
            BroadcastCanonChambers(actorIdent, chamberSet, canonCourse, pieceArr!);
        }
    }

    public void ReplaceMultiPieceCargo(
        uint actorIdent,
        Vector3 actorRealmSpot,
        Quaternion actorRealmRot,
        IReadOnlyList<ProxyShape> forms,
        uint phase,
        ActorImpactFlagSet flagSet,
        float realmShiftX,
        float realmShiftY,
        uint lbIdent,
        uint seedChamberIdent = 0u,
        bool isStatic = false,
        bool suspendIfNew = false,
        IReadOnlyList<ProxyShape>? pieceArr = null)
    {
        if (!_enrolments.TryGetValue(actorIdent, out EnrolmentRecord? preceding)
            || !preceding.IsMultiPart)
        {
            if (forms.Count is 0 && (pieceArr is null || pieceArr.Count is 0))
                return;
            EnrollMultiPiece(
                actorIdent,
                actorRealmSpot,
                actorRealmRot,
                forms,
                phase,
                flagSet,
                realmShiftX,
                realmShiftY,
                lbIdent,
                seedChamberIdent,
                isStatic,
                pieceArr: pieceArr);
            if (suspendIfNew)
                Suspend(actorIdent);
            return;
        }

        bool suspended = _dormantHolders.Contains(actorIdent);
        _formsByHolder[actorIdent] = forms;
        _enrolments[actorIdent] = preceding with
        {
            EntityWorldPos = actorRealmSpot,
            EntityWorldRot = actorRealmRot,
            State = phase,
            Flags = flagSet,
        };

        if (pieceArr is { Count: > 0 })
        {
            _canonPiecesByHolder[actorIdent] = pieceArr;
            if (_canonChambersByHolder.TryGetValue(actorIdent, out List<uint>? canonChambers)
                && canonChambers.Count is not 0)
            {
                DiscardCanonRanks(actorIdent, canonChambers);
                BroadcastCanonRanks(actorIdent, canonChambers, pieceArr);
                RepublishAffixedDescendants(actorIdent);
            }
        }

        if (suspended || !_chambersByHolder.TryGetValue(actorIdent, out List<uint>? chambers))
        {
            BumpHolderVer(actorIdent);
            return;
        }

        foreach (uint chamberIdent in chambers)
        {
            if (_ranksByChamber.TryGetValue(chamberIdent, out List<ProxyEntry>? listings))
                listings.RemoveAll(listing => listing.EntityId == actorIdent);
        }

        foreach (ProxyShape form in forms)
        {
            Vector3 pieceRealmSpot = actorRealmSpot
                + Vector3.Transform(form.OwnPlace, actorRealmRot);
            Quaternion pieceRealmRot = actorRealmRot * form.OwnSpin;
            ProxyEntry entry = new ProxyEntry(
                EntityId: actorIdent,
                GfxObjId: form.GfxObjId,
                Position: pieceRealmSpot,
                Rotation: pieceRealmRot,
                Radius: form.Radius,
                CollisionType: form.ImpactKind,
                CylHeight: form.CylHeight,
                Scale: form.Scale,
                State: phase,
                Flags: flagSet,
                LocalPosition: form.OwnPlace,
                LocalRotation: form.OwnSpin);
            foreach (uint chamberIdent in chambers)
                FileRank(entry, chamberIdent);
        }
        BumpHolderVer(actorIdent);
    }

    public void RefreshLocus(uint actorIdent, Vector3 realmSpot, Quaternion spin,
                               float realmShiftX, float realmShiftY, uint lbIdent,
                               uint seedChamberIdent = 0u)
    {
        if (!_enrolments.TryGetValue(actorIdent, out var reg))
            return;

        if (seedChamberIdent is 0u
            && ExteriorSeedFor(realmSpot, realmShiftX, realmShiftY, lbIdent) is 0u)
            return;

        _canonPiecesByHolder.TryGetValue(
            actorIdent,
            out IReadOnlyList<ProxyShape>? keptPieceArr);

        if (reg.IsMultiPart && _formsByHolder.TryGetValue(actorIdent, out var forms))
        {
            EnrollMultiPiece(actorIdent, realmSpot, spin, forms,
                              reg.State, reg.Flags, realmShiftX, realmShiftY, lbIdent,
                              seedChamberIdent, reg.IsStatic, pieceArr: keptPieceArr);
            return;
        }

        Register(actorIdent, reg.GfxObjId, realmSpot, spin, reg.Radius,
                 realmShiftX, realmShiftY, lbIdent,
                 reg.CollisionType, reg.CylHeight, reg.Scale,
                 reg.State, reg.Flags, seedChamberIdent, reg.IsStatic,
                 pieceArr: keptPieceArr);
    }

    public bool Suspend(uint actorIdent)
    {
        if (!_enrolments.ContainsKey(actorIdent))
            return false;

        if (_chambersByHolder.TryGetValue(actorIdent, out var chamberIdents))
        {
            _dormantHolderChambers[actorIdent] = new List<uint>(chamberIdents);
            foreach (uint chamberIdent in chamberIdents)
            {
                if (_ranksByChamber.TryGetValue(chamberIdent, out var roster))
                    DropHolderRanks(roster, actorIdent);
            }
            _chambersByHolder.Remove(actorIdent);
        }

        if (_canonChambersByHolder.TryGetValue(actorIdent, out List<uint>? canonChambers))
        {
            DiscardCanonRanks(actorIdent, canonChambers);
            _canonChambersByHolder.Remove(actorIdent);
            RepublishAffixedDescendants(actorIdent);
        }

        _dormantHolders.Add(actorIdent);
        BumpHolderVer(actorIdent);
        return true;
    }

    public void RefreshKineticsPhase(uint actorIdent, uint newPhase)
    {
        bool kept = _enrolments.TryGetValue(
            actorIdent,
            out EnrolmentRecord? keptEnrollment);
        if (kept)
        {
            _enrolments[actorIdent] = keptEnrollment! with { State = newPhase };
        }

        if (!_chambersByHolder.TryGetValue(actorIdent, out var chamberIdents))
        {
            if (kept)
                BumpHolderVer(actorIdent);
            return; // not registered - no-op

        }

        foreach (var chamberIdent in chamberIdents)
        {
            if (!_ranksByChamber.TryGetValue(chamberIdent, out var roster)) continue;
            for (int idx = 0; idx < roster.Count; ++idx)
            {
                if (roster[idx].EntityId == actorIdent)
                    roster[idx] = roster[idx] with { State = newPhase };
            }
        }

        if (kept)
            BumpHolderVer(actorIdent);

    }

    public void RefreshPwdBitfieldFlagSet(uint actorIdent, uint pwdBitfield)
    {
        var decoded = EntityContactFlagsExt.FromPwdBitfield(pwdBitfield);

        bool kept = _enrolments.TryGetValue(
            actorIdent,
            out EnrolmentRecord? keptEnrollment);
        if (kept)
        {
            var merged =
                (keptEnrollment!.Flags & ~EntityContactFlagsExt.PwdBitfieldDerivedBitmask)
                | decoded;
            if (merged == keptEnrollment.Flags)
                return;
            _enrolments[actorIdent] = keptEnrollment with { Flags = merged };
        }

        if (!_chambersByHolder.TryGetValue(actorIdent, out var chamberIdents))
        {
            if (kept)
                BumpHolderVer(actorIdent);
            return; // not registered - no-op
        }

        foreach (var chamberIdent in chamberIdents)
        {
            if (!_ranksByChamber.TryGetValue(chamberIdent, out var roster)) continue;
            for (int idx = 0; idx < roster.Count; ++idx)
            {
                if (roster[idx].EntityId == actorIdent)
                {
                    var merged =
                        (roster[idx].Flags & ~EntityContactFlagsExt.PwdBitfieldDerivedBitmask)
                        | decoded;
                    roster[idx] = roster[idx] with { Flags = merged };
                }
            }
        }

        if (kept)
            BumpHolderVer(actorIdent);
    }

    public void Deregister(uint actorIdent)
    {
        if (_descendantsOf.TryGetValue(actorIdent, out List<uint>? descendants)
            && descendants.Count > 0)
        {
            uint[] toUnfasten = descendants.ToArray();
            for (int idx = 0; idx < toUnfasten.Length; ++idx)
                UnfastenDescendantCore(toUnfasten[idx], dropFromAncestorRoster: false);
            _descendantsOf.Remove(actorIdent);
        }
        UnfastenDescendantCore(actorIdent, dropFromAncestorRoster: true);
        Withdraw(actorIdent, broadcastAlteration: true);
    }

    public void DeregisterStaticHoldersForLb(uint lbIdent)
    {
        uint[] holders = GrabStaticHoldersForLb(lbIdent);
        for (int idx = 0; idx < holders.Length; ++idx)
            DeregisterStaticHolderForLb(holders[idx], lbIdent);
    }

    public uint[] GrabStaticHoldersForLb(uint lbIdent)
    {
        uint stem = lbIdent & 0xFFFF0000u;
        List<uint> holders = new List<uint>();
        foreach (var (actorIdent, enrollment) in _enrolments)
        {
            if (enrollment.IsStatic
                && (enrollment.SeedCellId & 0xFFFF0000u) == stem)

                holders.Add(actorIdent);
        }
        holders.Sort();
        return holders.ToArray();
    }

    public void DeregisterStaticHolderForLb(
        uint actorIdent,
        uint lbIdent)
    {
        uint stem = lbIdent & 0xFFFF0000u;
        if (_enrolments.TryGetValue(
                actorIdent,
                out EnrolmentRecord? enrollment)
            && enrollment.IsStatic
            && (enrollment.SeedCellId & 0xFFFF0000u) == stem)

            Deregister(actorIdent);
    }

    private void EnrolRasterizeSole(
        uint actorIdent,
        Vector3 actorRealmSpot,
        Quaternion actorRealmRot,
        uint phase,
        ActorImpactFlagSet flagSet,
        float realmShiftX,
        float realmShiftY,
        uint lbIdent,
        uint seedChamberIdent,
        bool isStatic,
        bool broadcastAlteration,
        IReadOnlyList<ProxyShape> pieceArr)
    {
        // Flood FIRST - keep-when-empty, see Register
        uint seed = seedChamberIdent is not 0u
            ? seedChamberIdent
            : ExteriorSeedFor(actorRealmSpot, realmShiftX, realmShiftY, lbIdent);
        if (seed is 0u) return;

        (IReadOnlyList<uint> chamberSet, CanonCellSetRoute canonCourse) =
            CourseCanonChambers(
                seed,
                actorRealmSpot,
                actorRealmRot,
                phase,
                impactForms: Array.Empty<ProxyShape>(),
                pieceArr,
                isStatic);
        if (chamberSet.Count is 0) return; // keep-when-empty (pc:283540).

        Withdraw(actorIdent, broadcastAlteration: false);
        _formsByHolder[actorIdent] = Array.Empty<ProxyShape>();
        _enrolments[actorIdent] = new EnrolmentRecord(
            seed, actorRealmSpot, actorRealmRot, phase, flagSet, isStatic,
            IsMultiPart: true, GfxObjId: 0u, Radius: 0f,
            CollisionType: ProxyContactType.BSP, CylHeight: 0f, Scale: 1f);
        if (broadcastAlteration)
            BumpHolderVer(actorIdent);
        else
            ReindexHolderStems(actorIdent);

        _canonPiecesByHolder[actorIdent] = pieceArr;
        BroadcastCanonChambers(actorIdent, chamberSet, canonCourse, pieceArr);
    }

    private void Withdraw(uint actorIdent, bool broadcastAlteration)
    {
        bool existed = _enrolments.ContainsKey(actorIdent)
            || _chambersByHolder.ContainsKey(actorIdent)
            || _formsByHolder.ContainsKey(actorIdent)
            || _dormantHolders.Contains(actorIdent)
            || _dormantHolderChambers.ContainsKey(actorIdent);
        if (_chambersByHolder.TryGetValue(actorIdent, out var chamberIdents))
        {
            foreach (var chamberIdent in chamberIdents)
            {
                if (_ranksByChamber.TryGetValue(chamberIdent, out var roster))
                    DropHolderRanks(roster, actorIdent);
            }
            _chambersByHolder.Remove(actorIdent);
        }
        _formsByHolder.Remove(actorIdent);
        _enrolments.Remove(actorIdent);
        _dormantHolders.Remove(actorIdent);
        _dormantHolderChambers.Remove(actorIdent);
        _withdrawnByHolder.Remove(actorIdent);
        DiscardCanonChambers(actorIdent);
        if (existed && broadcastAlteration)
        {
            BumpHolderVer(actorIdent);
            DropHolderStems(actorIdent);
            _versions.Remove(actorIdent);
        }
    }
}
