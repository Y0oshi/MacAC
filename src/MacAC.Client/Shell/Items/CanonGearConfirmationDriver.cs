using MacAC.Client.Shell.Panels;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public sealed class CanonGearConfirmationDriver : IDisposable
{
    internal const string AvatarKillerMsg =
        "Using this altar will make you a player killer, able to attack or be attacked by other player killers. Are you sure you want to do this?";
    internal const string NonAvatarKillerMsg =
        "Using this altar will make you a non-player killer, unable to attack or be attacked by other player killers. Are you sure you want to do this?";
    internal const string VolatileRareMsg =
        "Are you sure you want to use this rare item?";

    private readonly CanonPromptMint _popups;
    private readonly GearDealingDriver _gearList;
    private bool _destroyed;

    public CanonGearConfirmationDriver(
        CanonPromptMint dialogs,
        GearDealingDriver items)
    {
        _popups = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _gearList = items ?? throw new ArgumentNullException(nameof(items));
        _gearList.PolicyActionRequested += OnRuleActAsked;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _gearList.PolicyActionRequested -= OnRuleActAsked;
    }

    private void OnRuleActAsked(ItemRulingAction act)
    {
        string? msg = act.Kind switch
        {
            ItemRulingActionKind.ConfirmPlayerKillerSwitch => AvatarKillerMsg,
            ItemRulingActionKind.ConfirmNonPlayerKillerSwitch => NonAvatarKillerMsg,
            ItemRulingActionKind.ConfirmVolatileRare => VolatileRareMsg,
            _ => null,
        };
        if (msg is null)
            return;

        var blob = CanonPromptData.Confirmation(msg)
            .Set(CanonPromptProperty.UsageObjectIdent, act.ObjectId);
        _popups.MakeDialog(blob, OnUsagePopupDone);
    }

    private void OnUsagePopupDone(CanonPromptData blob)
    {
        if (!blob.FetchBoolean(CanonPromptProperty.AckOutcome))
            return;
        uint objectIdent = blob.FetchUInt32(CanonPromptProperty.UsageObjectIdent);
        _gearList.PerformConfirmedUse(objectIdent);
    }
}
