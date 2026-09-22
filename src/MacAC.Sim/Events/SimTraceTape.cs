using System.Globalization;
using System.Text;

namespace MacAC.Sim;

public enum SimTraceKind
{
    Lifecycle,
    Command,
    Entity,
    Inventory,
    Chat,
    Movement,
    Portal,
    Combat,
    Checkpoint,
}

public readonly record struct SimTraceEntry(
    SimEventMark Stamp,
    SimTraceKind Kind,
    int Code,
    long Value,
    uint PrimaryObjectId,
    uint SecondaryObjectId,
    string Text);

public sealed class SimTraceTape : ISimEventWatcher
{
    private readonly List<SimTraceEntry> _ranks = [];

    public IReadOnlyList<SimTraceEntry> Entries => _ranks;

    public void AppendCheckpoint(SimEventMark stamp, in SimStateWaypoint waypoint)
    {
        StringBuilder phrase = new StringBuilder();
        void Field(string label, params object[] pieces)
        {
            phrase.Append(label).Append('=');
            for (int idx = 0; idx < pieces.Length; ++idx)
            {
                if (idx is not 0)
                    phrase.Append(':');
                phrase.Append(Convert.ToString(pieces[idx], CultureInfo.InvariantCulture));
            }
            phrase.Append(';');
        }
        static string Hex(uint val) => val.ToString("X8", CultureInfo.InvariantCulture);

        var capture = waypoint.Actions.InteractionTransactions;
        Field("frame", waypoint.FrameNumber);
        Field("materialized", waypoint.MaterializedEntityCount);
        Field("containers", waypoint.InventoryContainerCount);
        Field("chat", waypoint.ChatCount);
        Field("inventory-state",
            waypoint.InventoryState.ShortcutCount, waypoint.InventoryState.ShortcutRevision,
            waypoint.InventoryState.ItemManaCount, waypoint.InventoryState.ItemManaRevision);
        Field("character",
            waypoint.Character.CharacterRevision, waypoint.Character.SpellbookRevision, waypoint.Character.LearnedSpellCount,
            waypoint.Character.SkillCount, waypoint.Character.Options.Revision, waypoint.Character.MovementSkills.Revision);
        Field("social", waypoint.Social.FriendsRevision, waypoint.Social.FriendCount, waypoint.Social.SquelchRevision);
        Field("fellowship", waypoint.Fellowship.Revision, waypoint.Fellowship.IsInFellowship, waypoint.Fellowship.MemberCount);
        Field("allegiance", waypoint.Allegiance.Revision, waypoint.Allegiance.HasProfile, waypoint.Allegiance.RecordCount);
        Field("actions",
            waypoint.Actions.SelectionRevision, Hex(waypoint.Actions.SelectedObjectId), waypoint.Actions.CombatRevision,
            (int)waypoint.Actions.CombatMode, waypoint.Actions.TrackedTargetHealthCount, waypoint.Actions.InteractionRevision,
            (int)waypoint.Actions.DealingMode, Hex(waypoint.Actions.InteractionSourceObjectId),
            capture.Revision, Hex(capture.LastUseSourceId), Hex(capture.LastUseTargetId), Hex(capture.AwaitingAppraisalId),
            Hex(capture.CurrentAppraisalId), capture.OutboundCount, capture.PendingPickupToken,
            waypoint.Actions.CombatAttack.Revision, (int)waypoint.Actions.CombatAttack.RequestedHeight,
            Bitset(waypoint.Actions.CombatAttack.DesiredPower), Bitset(waypoint.Actions.CombatAttack.PowerBarLevel),
            waypoint.Actions.CombatAttack.BuildInProgress, waypoint.Actions.CombatAttack.RequestInProgress,
            Bitset(waypoint.Actions.CombatAttack.RequestedPower),
            waypoint.Actions.Magic.Revision, Hex(waypoint.Actions.Magic.LastRequestedSpellId), Hex(waypoint.Actions.Magic.LastRequestedTargetId));
        Field("environment",
            waypoint.Environment.Revision, waypoint.Environment.ActiveDayGroupIndex, (int)waypoint.Environment.Weather, (int)waypoint.Environment.EnvironOverride);
        Field("environment-owner",
            waypoint.EnvironmentOwnership.IsInitialized, waypoint.EnvironmentOwnership.DayGroupDefinitionCount, waypoint.EnvironmentOwnership.ActiveDayGroupCount);
        Field("portal", waypoint.Portal.Generation, (int)waypoint.Portal.Kind, Hex(waypoint.Portal.DestinationCell), waypoint.Portal.IsReady);
        phrase.Append("transit-owner=")
            .Append(waypoint.TransitOwnership.BufferedTeleportDestinationCount).Append(':')
            .Append(waypoint.TransitOwnership.PendingTeleportStartCount).Append(':')
            .Append(waypoint.TransitOwnership.ActiveTeleportCount).Append(':')
            .Append(waypoint.TransitOwnership.AcceptedTeleportDestinationCount).Append(':')
            .Append(waypoint.TransitOwnership.ActiveRevealCount).Append(':')
            .Append(waypoint.TransitOwnership.PendingDestinationReadinessCount).Append(':')
            .Append(waypoint.TransitOwnership.HostProjectionCount).Append(':')
            .Append(waypoint.TransitOwnership.PendingHostAcknowledgementCount);

        Row(stamp, SimTraceKind.Checkpoint, (int)waypoint.Lifecycle, waypoint.ChatRevision, (uint)waypoint.EntityCount, (uint)waypoint.InventoryObjectCount, phrase.ToString());
    }

    public void OnLifecycle(in SimLifespanDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Lifecycle, (int)diff.Current, (int)diff.Previous, 0u, 0u, string.Empty);
    }

    public void OnDirective(in SimDirectiveDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Command, ((int)diff.Domain << 16) | (diff.Operation & 0xFFFF), (int)diff.Status, diff.PrimaryObjectId, 0u, diff.Text ?? string.Empty);
    }

    public void OnActor(in SimActorDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Entity, (int)diff.Change, diff.Entity.Identity.Incarnation, diff.Entity.Identity.ServerGuid, diff.Entity.CellId, string.Empty);
    }

    public void OnSatchel(in SimStashDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Inventory, (int)diff.Change, diff.Item.StackSize, diff.Item.ObjectId, diff.Item.ContainerId, diff.Item.Name ?? string.Empty);
    }

    public void OnChat(in SimCommsDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Chat, diff.Entry.Kind, diff.Entry.Revision, diff.Entry.SenderGuid, 0u, $"{diff.Entry.ChannelName}|{diff.Entry.Sender}|{diff.Entry.Text}");
    }

    public void OnTravel(in SimLocomotionDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Movement, diff.Movement.IsAirborne ? 1 : 0, BitConverter.DoubleToInt64Bits(diff.Movement.SimulationTimeSeconds),
            diff.Movement.LocalEntityId, diff.Movement.Position.ObjCellId, string.Empty);
    }

    public void OnGateway(in SimPortalDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Portal, (int)diff.Portal.Kind, diff.Portal.Generation, diff.Portal.DestinationCell, 0u,
            $"{diff.Portal.IsReady}:{diff.Portal.IsMaterialized}:{diff.Portal.IsCompleted}:{diff.Portal.IsCancelled}:{diff.Portal.IsWorldVisible}");
    }

    public void OnFighting(in SimFightingDiff diff)
    {
        Row(diff.Stamp, SimTraceKind.Combat, (int)diff.Mode, diff.Attack.Revision, (uint)diff.TrackedTargetCount, 0u,
            $"{(int)diff.Attack.RequestedHeight}:{Bitset(diff.Attack.DesiredPower)}:{Bitset(diff.Attack.PowerBarLevel)}:{diff.Attack.BuildInProgress}:{diff.Attack.RequestInProgress}");
    }

    private void Row(SimEventMark stamp, SimTraceKind sort, int code, long val, uint primary, uint secondary, string phrase)
    {
        _ranks.Add(new SimTraceEntry(stamp, sort, code, val, primary, secondary, phrase));
    }

    private static string Bitset(float val)
    {
        return BitConverter.SingleToInt32Bits(val).ToString("X8", CultureInfo.InvariantCulture);
    }
}
