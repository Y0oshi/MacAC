namespace MacAC.Mechanics.Kinetics.Gait;

/// <summary>An object that wants to hear where another one goes, and how often.</summary>
public sealed class WatcherFacts
{
    internal IKineticObjHost WatcherHub { get; }

    public uint ObjectId { get; }

    public double Quantum { get; set; }

    public float Radius { get; set; }

    public Locus PreviousSentLocus { get; set; }

    public WatcherFacts(uint objectIdent, float radius, double quantum, IKineticObjHost watcherHost)
    {
        ArgumentNullException.ThrowIfNull(watcherHost);
        if (watcherHost.Id != objectIdent)
            throw new ArgumentException("Watcher identity must match the voyeur object ID", nameof(watcherHost));

        ObjectId = objectIdent;
        Radius = radius;
        Quantum = quantum;
        WatcherHub = watcherHost;
    }
}
