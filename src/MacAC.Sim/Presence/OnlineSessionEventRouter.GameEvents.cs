using MacAC.Mechanics.Comms;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Sim.Play;

namespace MacAC.Sim.Presence;

public sealed partial class OnlineSessionEventRouter
{
    private void WirePlaySignals()
    {
        var satchel = _satchel;
        var character = _toon;
        var social = _social;
        var toon = character.Character;
        Func<uint> self = satchel.PlayerGuid;
        var fellows = social.Fellowship;
        var allegiance = social.Allegiance;
        var barter = social.Trade;
        var house = social.House;
        var contracts = social.Contracts;

        Own(GameEventBindings.WireAll(
            _session.PlaySignals,
            satchel.Objects,
            character.Combat,
            toon.Spellbook,
            social.Chat,
            toon.LocalPlayer,
            social.TurbineChat,
            onAptitudesUpdated: (execAptitude, leapAptitude) =>
            {
                toon.RefreshTravelAptitudeBase(execAptitude, leapAptitude);
                character.OnSkillsUpdated?.Invoke(execAptitude, leapAptitude);
                character.OnMovementStatsUpdated?.Invoke();
            },
            locateAptitudeEquationBonus: character.ResolveSkillFormulaBonus,
            onShortcuts: satchel.OnShortcuts,
            avatarOid: self,
            onUseDone: satchel.OnUseDone,
            onAppraisal: satchel.OnAppraisal,
            gearMana: satchel.ItemMana,
            onAckReq: character.OnConfirmationRequest,
            onAckDone: character.OnConfirmationDone,
            friends: social.Friends,
            squelch: social.Squelch,
            onWantedModules: null,
            onToonKnobs: (options1, options2, trailerTruncated) =>
            {
                if (trailerTruncated)
                    return;
                toon.Options.Replace(options1, options2, armSrvSeed: true);
                character.OnCharacterOptionsChanged?.Invoke(options1, options2);
            },
            clientMoment: character.ClientTime,
            externalVessels: satchel.ExternalContainers,
            merchant: satchel.Vendor,
            onInterfacePhrase: social.AddText,
            accepting: IsAccepting,
            onFellowshipWholeRefresh: fellows is null ? null : fellows.ImposeWholeRefresh,
            onFellowshipRefreshFellow: fellows is null ? null : fellows.ImposeRefreshFellow,
            onFellowshipQuit: fellows is null ? null : quitter => fellows.ImposeQuit(quitter, self()),
            onFellowshipDismiss: fellows is null ? null : dismissed => fellows.ImposeDismiss(dismissed, self()),
            onFellowshipDisband: fellows is null ? null : fellows.ImposeDisband,
            onAllegianceRefresh: allegiance is null ? null : allegiance.ImposeRefresh,
            onAllegianceRefreshDone: allegiance is null ? null : allegiance.ImposeRefreshDone,
            onAllegianceRefreshAborted: allegiance is null ? null : allegiance.ImposeRefreshAborted,
            onAllegianceSigninNotification: allegiance is null ? null : notice => allegiance.ImposeSigninNotification(notice.CharacterGuid, notice.IsLoggedIn),
            onBarterEnroll: barter is null ? null : refresh => barter.ImposeEnroll(refresh, self()),
            onBarterShut: barter is null ? null : _ =>
            {
                barter.EnactShut();
                social.AddText?.Invoke(TextRefusals.BarterCancelled, CanonLogTextType.ClientLocal);
            },
            onBarterAppend: barter is null ? null : barter.ImposeAppend,
            onBarterDrop: barter is null ? null : barter.ImposeDrop,
            onBarterAdmit: barter is null ? null : who => barter.ImposeAdmit(who, self()),
            onBarterDecline: barter is null ? null : who => barter.ImposeDecline(who, self()),
            onBarterRestart: barter is null ? null : _ => barter.ImposeRestart(),
            onBarterMiss: barter is null ? null : barter.ImposeMiss,
            onBarterWipeAcceptance: barter is null ? null : barter.ImposeWipeAcceptance,
            onHouseBlob: house is null ? null : blob => house.ImposeHouseBlob(blob, self()),
            onHouseCondition: house is null ? null : weenieProblem => house.ImposeHouseCondition(weenieProblem, self()),
            onHouseRefreshRentMoment: house is null ? null : rentMoment => house.ImposeRentMoment(rentMoment, self()),
            onHouseRefreshRentPayment: house is null ? null : rent => house.ImposeRentPayment(rent, self()),
            onContractChart: contracts is null ? null : contracts.ImposeChart,
            onContractRefresh: contracts is null ? null : contracts.EnactRefresh,
            onToonBannerChart: (readoutBannerIdent, bannerIdents) => toon.Titles.ReplaceChart(readoutBannerIdent, bannerIdents),
            onRefreshBanner: (bannerIdent, setAsReadout) => toon.Titles.ImposeRefreshBanner(bannerIdent, setAsReadout)));
    }
}
