namespace MacAC.Client.Link;

internal interface IOnlineInRealmSource
{
    bool IsInWorld { get; }
}

internal interface IOnlineRealmSessionSource
{
    MacAC.Wire.RealmSession? LatestSess { get; }
}

internal interface IOnlineWidgetSessionTarget : IOnlineInRealmSource, IOnlineRealmSessionSource
{
    MacAC.Sim.Comms.IDirectiveBus Commands { get; }
}
