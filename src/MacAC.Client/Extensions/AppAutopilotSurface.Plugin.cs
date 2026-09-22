using MacAC.Client.SimBridge;
using MacAC.Extensibility.Automation;
using MacAC.Extensibility.Hosting;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Avatar;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Traits;
using MacAC.Sim;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{
    internal ISlashCommandRegistry ExtensionDirectives => _extensionDirectives;

    public NavigationOutcome WipeTravelIntent()
    {
        CurrentGameEngineBridge? directives;
        lock (_latch)
            directives = _sessDirectives;
        if (directives is null || !IsAvailable)
            return NavigationOutcome.Unavailable;
        var outcome = directives.MovementCommands.WipeIntent(
            directives.Generation);
        return outcome.Status == SimDirectiveStatus.Accepted
            ? NavigationOutcome.Accepted
            : NavigationOutcome.Rejected;
    }

    public ISelfSheet Character => this;

    public int ToonOrdinal
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core is null
                || !core.CharacterSelection.TryGet(
                    core.AvatarIdentity.ServerGuid,
                    out SimToonPickEntry toon)
                ? -1
                : toon.ActiveIndex;
        }
    }

    public ISpellbook Spells => this;

    public ICastingControls Magic => this;

    public IChatControls Chat => this;

    public ICombatControls Combat => this;

    public IEquipmentControls Equipment => this;

    public IItemControls Items => this;

    public ILootControls Loot => this;

    public IFellowshipControls Fellowship => this;

    public string FellowshipLabel
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.Fellowship.Snapshot.Name ?? string.Empty;
        }
    }

    public IEnchantmentControls Enchantments => this;

    public INavigationControls Navigation => this;

    public IWorldObjectControls Objects => this;

    public IWorldTimeControls RealmMoment => this;

    public string RealmLabel
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.CharacterSelection.Snapshot.WorldName ?? string.Empty;
        }
    }

    public ILoginControls Login => this;

    public INetworkControls Network => this;

    public IRecoveryControls Recovery => this;

    public IProjectileControls Missiles => this;

    public ISelectionControls Selection => this;

    public NavigationOutcome AssignTravelIntent(
        in MovementIntent intent)
    {
        CurrentGameEngineBridge? directives;
        lock (_latch)
            directives = _sessDirectives;
        if (directives is null || !IsAvailable)
            return NavigationOutcome.Unavailable;
        var outcome = directives.MovementCommands.AssignIntent(
            directives.Generation,
            new LocomotionInput(
                intent.Forward,
                intent.Backward,
                intent.StrafeLeft,
                intent.StrafeRight,
                intent.TurnLeft,
                intent.TurnRight,
                intent.Run,
                MouseDeltaX: 0f,
                intent.Jump));
        return outcome.Status == SimDirectiveStatus.Accepted
            ? NavigationOutcome.Accepted
            : NavigationOutcome.Rejected;
    }

    public FellowshipVerdict AssignOpen(bool isOpen)
    {
        return CallFellowship(directives => directives.FellowshipDirectives.SetOpen(
            directives.Generation,
            isOpen));
    }

    public void Unbind()
    {
        lock (_latch)
            UnfastenBolted();
        _peers.Withdraw();
        RecognizedSelfBuffs = Array.Empty<SpellFacts>();
        RecognizedAssaultArcana = Array.Empty<SpellFacts>();
        RecognizedFightingArcana = Array.Empty<SpellFacts>();
        EngagedEnchantments = Array.Empty<ActiveEnchantmentFacts>();
    }

    uint ILoginControls.NextLoginObjectId
    {
        get
        {
            lock (_latch)
                return _runtime?.Session.UpcomingSigninToonIdent ?? 0u;
        }
    }

    public void OnComms(in SimCommsEvent diff)
    {
        lock (_latch)
        {
            if (_destroyed || _communication is null)
                return;
            var listing = diff.Entry;
            _commsMsgs.Add(new ChatLine(
                ++_extensionCommsSeries,
                listing.SenderGuid,
                listing.Kind,
                listing.Sender,
                listing.Text,
                listing.ChannelName));
            if (_commsMsgs.Count > CeilingExtensionCommsMsgs)
            {
                _commsMsgs.RemoveRange(
                    0,
                    _commsMsgs.Count - CeilingExtensionCommsMsgs);
            }
        }
    }

    internal bool TryHndExtensionDirective(string directiveStroke) =>
        _extensionDirectives.TryHnd(directiveStroke);

    RecoveryVerdict IRecoveryControls.ClearOneBusyReference()
    {
        SimCore? core;
        lock (_latch)
        {
            if (_destroyed)
                return new(false, Message: "The plugin host is disposed.");
            core = _runtime;
        }
        if (core is null)
            return new(false, Message: "No game session is bound.");

        var transactions =
            core.SatchelHolder.Transactions;
        int prior = transactions.OccupiedCount;
        transactions.FinishUse(0u);
        return new(
            Accepted: true,
            PreviousCount: prior,
            CurrentCount: transactions.OccupiedCount,
            Message: prior is 0
                ? "The action busy count was already zero."
                : "Cleared one action busy reference.");
    }

    WorldTimeFrame IWorldTimeControls.Snapshot
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            if (core is null || !IsAvailable)
                return default;

            double rawBeats = core.SurroundingsHolder.WorldTime.InstantBeats;
            var calendar = core.SurroundingsHolder.WorldTime.Calendar;
            DerethDateMoment.Almanac val = calendar.ToCalendar(rawBeats);
            int hour = (int)val.Hour;
            bool isDay = hour is >= 4 and < 12;
            double shiftedBeats = Math.Max(0d, rawBeats)
                + calendar.OriginShiftBeats;
            double playBeats = shiftedBeats
                + DerethDateMoment.ZeroYear * DerethDateMoment.YearBeats;
            double withinHour = shiftedBeats
                - Math.Floor(shiftedBeats / DerethDateMoment.HourBeats)
                    * DerethDateMoment.HourBeats;
            double untilNight = isDay
                ? ((12 - hour) * DerethDateMoment.HourBeats - withinHour) / 60d
                : 0d;
            int dayHour = hour <= 4 ? hour + 16 : hour;
            double untilDay = !isDay
                ? ((20 - dayHour) * DerethDateMoment.HourBeats - withinHour) / 60d
                : 0d;
            return new WorldTimeFrame(
                true,
                playBeats,
                val.Year,
                (int)val.Month,
                val.Day,
                hour,
                ComposeCalendarLabel(val.Month.ToString()),
                ComposeCalendarLabel(val.Hour.ToString()),
                isDay,
                Math.Max(0d, untilDay),
                Math.Max(0d, untilNight));
        }
    }

    NavigationFrame INavigationControls.Snapshot
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            if (core is null || !IsAvailable)
                return default;

            var travel = core.Movement.Snapshot;
            if (!travel.HasController)
                return default;
            var gateway = core.Portal.Snapshot;
            var onlineLocus =
                ProjectNavigationLocus(travel.Position);
            var confirmedLocus = onlineLocus;
            ulong confirmedRev = 0UL;
            if (core.EntityObjects.Entities.TryFetchEngaged(
                    core.AvatarIdentity.ServerGuid,
                    out SimActorRecord ownCapture)
                && TranslateLocus(ownCapture.Snapshot.Position) is { } approved)
            {
                confirmedLocus = ProjectNavigationLocus(approved);
                confirmedRev = ownCapture.PositionAuthorityVersion;
            }
            return new NavigationFrame(
                IsAvailable: true,
                IsPortalSpace: gateway.Kind != SimPortalKind.None
                    && !gateway.Completed
                    && !gateway.Cancelled,
                LocalObjectId: core.AvatarIdentity.ServerGuid,
                Position: onlineLocus,
                IsMoving: travel.Velocity.LengthSquared() > 0.0001f
                    || travel.HasCommandInput,
                IsAirborne: travel.IsAirborne)
            {
                ConfirmedLocus = confirmedLocus,
                ConfirmedLocusRev = confirmedRev,
            };
        }
    }

    public CombatFrame Snapshot
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            if (core is null || !IsAvailable)
                return default;

            var act = core.ActHolder.View.Snapshot;
            var assault = act.CombatAttack;
            return new CombatFrame(
                act.SelectedObjectId,
                Project(act.CombatMode),
                Project(assault.RequestedHeight),
                assault.DesiredPower,
                assault.PowerBarLevel,
                assault.BuildInProgress,
                assault.RequestInProgress,
                assault.ServerResponsePending,
                assault.RepeatAttackInProgress)
            {
                WrapUpRev = assault.WrapUpRevision,
                WrapUpSeries = assault.WrapUpSequence,
                WrapUpWeenieProblem = assault.WrapUpWeenieError,
            };
        }
    }

    bool ILoginControls.ClearNextLogin()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        return core?.Session.WipeUpcomingSignin() == true;
    }

    bool ILoginControls.SetNextLogin(uint toonObjectIdent)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        return core?.Session.TrySetUpcomingSignin(toonObjectIdent) == true;
    }

    private static string ComposeCalendarLabel(string val)
    {
        return val
        .Replace("AndHalf", "-and-Half", StringComparison.Ordinal);
    }

    private void UnfastenBolted()
    {
        if (_runtime is { } core)
        {
            core.SatchelHolder.Transactions.RequestFailed -=
                OnSatchelReqFailed;
            core.SatchelHolder.Transactions.RequestCompleted -=
                OnSatchelReqFinished;
        }
        _communicationSubscription?.Dispose();
        _communicationSubscription = null;
        _commsMsgs.Clear();
        if (_grimoire is not null)
        {
            _grimoire.SpellbookChanged -= OnGrimoireAltered;
            _grimoire.EnchantmentsChanged -= OnEnchantmentsAltered;
        }
        _grimoire = null;
        _toon = null;
        _casting = null;
        _runtime = null;
        _communication = null;
        _dismissGhost = null;
        _followedEnchantments.Clear();
        _followedCastingWrapUpRev = 0;
        _missileDiagSpecimens = Array.Empty<ProjectileTraceSample>();
        _missileDiagSpecimensExpireAt = 0;
    }

    private void OnCounterpartBeat(double passedSecs)
    {
        _counterpartHeartbeatLeftover -= Math.Max(0d, passedSecs);
        if (_counterpartHeartbeatLeftover > 0d)
            return;
        _counterpartHeartbeatLeftover = CounterpartHeartbeatSecs;
        BroadcastCounterpartCapture();
    }

    private void OnSatchelReqFinished(PackRequestInFlight req)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _previousSatchelWrapUp = new InventoryReceipt(
                ++_satchelWrapUpRev,
                Project(req.Kind),
                req.ItemId,
                0u);
        }
    }

    private void OnSatchelReqFailed(
        PackRequestInFlight req,
        uint weenieProblem)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _previousSatchelWrapUp = new InventoryReceipt(
                ++_satchelWrapUpRev,
                Project(req.Kind),
                req.ItemId,
                weenieProblem);
        }
    }

    private void OnGrimoireAltered() => ReassembleGrimoire();

    private void OnEnchantmentsAltered() => ReassembleEnchantments();

    private void BroadcastCounterpartCapture()
    {
        if (!IsAvailable)
        {
            _peers.Withdraw();
            return;
        }

        ISelfSheet toon = this;
        var navigation =
            ((INavigationControls)this).Snapshot;
        if (!navigation.IsAvailable || toon.ObjectId is 0u)
        {
            _peers.Withdraw();
            return;
        }

        try
        {
            _peers.Publish(new PeerEntry(
                _peers.ClientId,
                toon.ObjectId,
                toon.Name,
                toon.RealmLabel,
                navigation.Position,
                _counterpartTags,
                toon.LatestHealth,
                toon.LatestMana,
                toon.LatestStamina,
                toon.MaxHealth,
                toon.UpperMana,
                toon.UpperStamina,
                navigation.Position.HeadingDegrees));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ReassembleGrimoire()
    {
        Grimoire? grimoire;
        lock (_latch)
            grimoire = _grimoire;
        if (grimoire is null)
        {
            RecognizedSelfBuffs = Array.Empty<SpellFacts>();
            RecognizedAssaultArcana = Array.Empty<SpellFacts>();
            RecognizedFightingArcana = Array.Empty<SpellFacts>();
            return;
        }

        List<SpellFacts> buffs = new List<SpellFacts>();
        List<SpellFacts> assaults = new List<SpellFacts>();
        List<SpellFacts> fighting = new List<SpellFacts>();
        foreach (uint arcanumIdent in grimoire.LearnedArcana)
        {
            if (!grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta meta))
                continue;
            if (meta.IsOffensive || meta.IsDebuff)
                fighting.Add(Project(meta));
            if (!meta.IsBeneficial || meta.IsDebuff || meta.IsUntargeted)
            {
                if (meta.IsOffensive
                    && !meta.IsDebuff
                    && !meta.IsBeneficial
                    && !meta.IsSelfTargeted
                    && !meta.IsUntargeted
                    && meta.TargetMask is not 0u)

                    assaults.Add(Project(meta));
                continue;
            }
            buffs.Add(Project(meta));
        }

        buffs.Sort(static (facts, b) =>
            facts.Family != b.Family
                ? facts.Family.CompareTo(b.Family)
                : b.Tier.CompareTo(facts.Tier));
        assaults.Sort(static (facts, b) =>
        {
            int tier = b.Tier.CompareTo(facts.Tier);
            return tier is not 0
                ? tier
                : b.Difficulty.CompareTo(facts.Difficulty);
        });
        RecognizedSelfBuffs = buffs;
        RecognizedAssaultArcana = assaults;
        fighting.Sort(static (facts, b) =>
        {
            int tier = b.Tier.CompareTo(facts.Tier);
            return tier is not 0
                ? tier
                : string.CompareOrdinal(facts.Name, b.Name);
        });
        RecognizedFightingArcana = fighting;
    }

    private void ReassembleEnchantments()
    {
        Grimoire? grimoire;
        lock (_latch)
            grimoire = _grimoire;
        if (grimoire is null)
        {
            EngagedEnchantments = Array.Empty<ActiveEnchantmentFacts>();
            return;
        }

        var engaged =
            grimoire.EnchantmentsInFxCapture;
        var built = new List<ActiveEnchantmentFacts>(engaged.Count);
        foreach (LiveEnchantmentRow capture in engaged)
        {
            ReassembleEnchantmentsLoop(grimoire, capture, built);
        }
        EngagedEnchantments = built;
    }

    private void ReassembleEnchantmentsLoop(Grimoire grimoire, LiveEnchantmentRow capture, List<ActiveEnchantmentFacts> built)
    {
        uint clan = 0;
        int tier = 0;
        if (grimoire.TryFetchMetadata(capture.SpellId, out SpellMeta meta))
        {
            clan = meta.Family;
            tier = meta.Generation;
        }
        built.Add(new ActiveEnchantmentFacts(
                        capture.SpellId, clan, tier, capture.Duration));
    }

    private static uint SchoolAptitudeIdent(MechMagicSchool school)
    {
        return school switch
        {
            MechMagicSchool.CreatureEnchantment => 31u,
            MechMagicSchool.ItemEnchantment => 32u,
            MechMagicSchool.LifeMagic => 33u,
            MechMagicSchool.WarMagic => 34u,
            MechMagicSchool.VoidMagic => 43u,
            _ => 0u,
        };
    }

    public string Name
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            if (core is null)
                return string.Empty;
            uint avatarIdent = core.AvatarIdentity.ServerGuid;
            return core.SatchelHolder.Objects.Get(avatarIdent)?.Name
                ?? string.Empty;
        }
    }

    string IFellowshipControls.Name => FellowshipLabel;

    public string AcctLabel
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.CharacterSelection.Snapshot.AccountName ?? string.Empty;
        }
    }

    public int Level
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core is null
                ? 0
                : core.SatchelHolder.Objects
                .Get(core.AvatarIdentity.ServerGuid)?
                .Properties.FetchInt((uint)TraitInt.Level) ?? 0;
        }
    }

    public int PrimaryBundleSpareSockets
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            if (core is null)
                return 0;
            uint avatarIdent = core.AvatarIdentity.ServerGuid;
            int occupied = core.SatchelHolder.Objects.Objects.Count(gear =>
                gear.VesselTag == avatarIdent
                && ClassifyObject(gear) is not (
                    EntityClass.Container or EntityClass.Foci)
                && gear.CurrentlyEquippedLocale == 0);
            return Math.Max(0, 102 - occupied);
        }
    }

    public uint ObjectId
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.Lifecycle.PlayerGuid ?? 0u;
        }
    }

    public uint LatestHealth => Vital(SelfState.VitalSort.Health).Current;

    public uint LatestStamina => Vital(SelfState.VitalSort.Stamina).Current;

    public uint LatestMana => Vital(SelfState.VitalSort.Mana).Current;

    uint ILootControls.CurrentContainerId
    {
        get
        {
            lock (_latch)
                return _runtime?.SatchelHolder.ExternalVessels
                    .LatestVesselIdent ?? 0u;
        }
    }

    public uint MaxHealth => Vital(SelfState.VitalSort.Health).Maximum;

    public uint UpperStamina => Vital(SelfState.VitalSort.Stamina).Maximum;

    public uint UpperMana => Vital(SelfState.VitalSort.Mana).Maximum;

    public int SummoningMastery
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            if (core is null)
                return 0;
            uint avatarIdent = core.AvatarIdentity.ServerGuid;
            return core.SatchelHolder.Objects.Get(avatarIdent)?.Properties.FetchInt(
                (uint)TraitInt.SummoningMastery) ?? 0;
        }
    }

    private (uint Current, uint Maximum) Vital(SelfState.VitalSort sort)
    {
        SimToonLedger? toon;
        lock (_latch)
            toon = _toon;
        if (toon is null
            || !toon.View.TryFetchVital((int)sort, out var vital))

            return (0, 0);
        return (vital.Current, vital.Maximum);
    }

    public IReadOnlyList<ActiveEnchantmentFacts> EngagedEnchantments { get; private set; } = Array.Empty<ActiveEnchantmentFacts>();

    int IItemControls.ActiveOwnedPetCount
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            if (core is null || !IsAvailable)
                return 0;
            uint avatarIdent = core.AvatarIdentity.ServerGuid;
            int tally = 0;
            foreach (ClientThing contender in core.SatchelHolder.Objects.Objects)
            {
                if (contender.PetHolderIdent == avatarIdent
                    && (contender.Type & GearKind.Creature) != 0)

                    ++tally;
            }
            return tally;
        }
    }

    public uint EngagedMerchantObjectIdent
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.SatchelHolder.Vendor.MerchantIdent ?? 0u;
        }
    }

    public IReadOnlyList<SkillFacts> Skills
    {
        get
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
            if (toon is null || labels.Count is 0)
                return Array.Empty<SkillFacts>();

            List<SkillFacts> built = new List<SkillFacts>(labels.Count);
            foreach (KeyValuePair<uint, string> duo in labels)
            {
                uint glyphIdent = glyphs.TryGetValue(duo.Key, out uint glyph) ? glyph : 0u;
                if (TryProjectAptitude(toon, duo.Key, duo.Value, glyphIdent, out SkillFacts aptitude))
                    built.Add(aptitude);
            }
            built.Sort(static (facts, b) => string.CompareOrdinal(facts.Name, b.Name));
            return built;
        }
    }

    private static bool TryProjectAptitude(
        SimToonLedger toon, uint aptitudeIdent, string label, uint glyphIdent,
        out SkillFacts aptitude)
    {
        if (!toon.View.TryFetchSkill(aptitudeIdent, out var capture))
        {
            aptitude = default;
            return false;
        }
        uint baseTier = capture.CurrentLevel;
        uint latestTier = checked((uint)Math.Max(
            0,
            toon.LocalPlayer.FetchNetAptitude(aptitudeIdent)
                ?? checked((int)baseTier)));
        aptitude = new SkillFacts(
            aptitudeIdent, label, Training(capture.Status), latestTier)
        {
            Base = baseTier,
            IconId = glyphIdent,
        };
        return true;
    }

    private static SkillTraining Training(uint condition)
    {
        return condition switch
        {
            1 => SkillTraining.Untrained,
            2 => SkillTraining.Trained,
            3 => SkillTraining.Specialized,
            _ => SkillTraining.Unknown,
        };
    }

    public IReadOnlyList<AttributeFacts> Attributes
    {
        get
        {
            SimToonLedger? toon;
            lock (_latch)
                toon = _toon;
            if (toon is null)
                return Array.Empty<AttributeFacts>();

            List<AttributeFacts> built = new List<AttributeFacts>(AttrLabels.Length);
            for (int sort = 0; sort < AttrLabels.Length; ++sort)
            {
                if (toon.View.TryFetchAttr(sort, out var attr))
                {
                    uint net = checked((uint)Math.Max(
                        0,
                        toon.LocalPlayer.FetchNetAttr(
                            (SelfState.StatKind)sort)
                            ?? checked((int)attr.Current)));
                    built.Add(new AttributeFacts(
                        sort, AttrLabels[sort], net)
                    {
                        Base = attr.Current,
                    });
                }
            }
            return built;
        }
    }
}
