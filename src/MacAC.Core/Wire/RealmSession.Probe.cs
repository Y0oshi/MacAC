using System.Diagnostics;
using MacAC.Wire.Transport;

namespace MacAC.Wire;

public sealed partial class RealmSession
{
    // A cumulative counter's last-seen value, for per-window deltas
    private struct Window
    {
        private long _observed;

        public long Take(long cumulative)
        {
            long diff = cumulative - _observed;
            _observed = cumulative;
            return diff;
        }
    }

    private long _sensorPreviousBeatTs;
    private long _sensorPaneBeginTs;
    private long _sensorUpperGapBeats;
    private int _sensorProcessedPane;
    private int _sensorAllowanceBreaks;
    private int _sensorTransmitPane;
    private int _sensorIncomingZDepth;
    private Window _wAcks, _wResends, _wNaksOut, _wNaksIn, _wRejsIn, _wDupDrops, _wShelved, _wReclaimed;

    private void InspectNetBeatCadence(long beatBeginTs, int processed, bool allowanceBroke)
    {
        if (_sensorPreviousBeatTs is not 0)
            _sensorUpperGapBeats = Math.Max(_sensorUpperGapBeats, beatBeginTs - _sensorPreviousBeatTs);
        _sensorPreviousBeatTs = beatBeginTs;
        _sensorProcessedPane += processed;
        if (allowanceBroke)
            ++_sensorAllowanceBreaks;

        if (_sensorPaneBeginTs is 0)
        {
            _sensorPaneBeginTs = beatBeginTs;
            return;
        }
        long paneBeats = beatBeginTs - _sensorPaneBeginTs;
        if (paneBeats < Stopwatch.Frequency)
            return;

        var connect = _conveyance;
        LinkStats? stats = connect?.Stats;
        Console.WriteLine(ComposeNetBeatStroke(
            (double)paneBeats / Stopwatch.Frequency,
            _sensorProcessedPane,
            Volatile.Read(ref _sensorIncomingZDepth),
            _sensorAllowanceBreaks,
            _sensorUpperGapBeats * 1000.0 / Stopwatch.Frequency,
            Interlocked.Exchange(ref _sensorTransmitPane, 0),
            _wAcks.Take(stats?.AcksSent ?? 0),
            _wResends.Take(stats?.ResendsSent ?? 0),
            _wNaksOut.Take(stats?.NaksSent ?? 0),
            _wNaksIn.Take(stats?.NakReqsReceived ?? 0),
            _wRejsIn.Take(stats?.RejectsReceived ?? 0),
            _wDupDrops.Take(stats?.IncomingDupsDropped ?? 0),
            _wShelved.Take(stats?.TagsShelved ?? 0),
            _wReclaimed.Take(stats?.RejectWordsReclaimed ?? 0),
            stats?.StashZDepth ?? 0,
            connect?.Inbound.NakTally ?? 0,
            LatestPhase));

        _sensorPaneBeginTs = beatBeginTs;
        _sensorUpperGapBeats = 0;
        _sensorProcessedPane = 0;
        _sensorAllowanceBreaks = 0;
    }

    internal static string ComposeNetBeatStroke(
        double paneSecs,
        int processed,
        int fifoZDepth,
        int allowanceBreaks,
        double upperGapMsec,
        int sends,
        long acks,
        long resends,
        long naksOut,
        long naksIn,
        long rejsIn,
        long dupDrops,
        long shelved,
        long reclaimed,
        int stashZDepth,
        int nakSetZDepth,
        State phase)
    {
        return $"[net-tick] in/s={processed / paneSecs:F0}"
        + $" q={fifoZDepth}"
        + $" budget-breaks={allowanceBreaks}"
        + $" maxgap={upperGapMsec:F0}ms"
        + $" out/s={sends / paneSecs:F0}"
        + $" acks/s={acks / paneSecs:F0}"
        + $" resend/s={resends / paneSecs:F0}"
        + $" nak-out/s={naksOut / paneSecs:F0}"
        + $" nak-in/s={naksIn / paneSecs:F0}"
        + $" rej-in/s={rejsIn / paneSecs:F0}"
        + $" dup-drop/s={dupDrops / paneSecs:F0}"
        + $" parked/s={shelved / paneSecs:F0}"
        + $" reclaim/s={reclaimed / paneSecs:F0}"
        + $" cache={stashZDepth}"
        + $" nakset={nakSetZDepth}"
        + $" st={phase}";
    }
}
