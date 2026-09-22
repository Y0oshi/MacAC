using MacAC.Assets;
using Microsoft.Extensions.Logging;

namespace MacAC.Client.Graphics.Batching;

public partial class ThingTriMeshKeeper
{
    private void ProcessQueue()
    {
        while (true)
        {
            _prepJobOnHand.Wait();
            PreparationAsk req;

            lock (_queuedReqs)
            {
                var wakeAct = DecidePrepWorkerWake(
                    IsDisposed,
                    _queuedReqs.Count is not 0,
                    _linedTriMeshBlob.IsAtHiWater,
                    _arenaBackpressured);
                if (wakeAct == PrepWorkerWakeAct.Exit)
                    return;
                if (wakeAct == PrepWorkerWakeAct.ResetAndWait)
                {
                    _prepJobOnHand.Reset();
                    continue;
                }

                var joint = _queuedReqs.Last;
                while (joint is not null && _engagedPrepByIdent.ContainsKey(joint.Value.Id))
                    joint = joint.Previous;
                if (joint is null)
                {
                    _prepJobOnHand.Reset();
                    continue;
                }
                req = joint.Value;
                _queuedReqs.Remove(joint);
                _queuedReqByIdent.Remove(req.Id);
                _engagedPrepByIdent.Add(req.Id, req);
            }

            ulong ident = req.Id;
            var tcs = req.Completion;
            var token = req.Abort.Token;

            HarvestedMesh? wrapUpOutcome = null;
            Exception? wrapUpProblem = null;
            bool wrapUpCanceled = false;
            var wrapUpAbort = token;

            try
            {
                if (token.IsCancellationRequested)
                {
                    wrapUpCanceled = true;
                }
                else
                {

                    var blob = req.StashedBlob;
                    if (blob is null)
                    {
                        var scan =
                            _preparedAssets.Read(req.Asset, token);
                        blob = scan.Data;
                        if (scan.Status != BakedAssetReadStatus.Loaded)
                        {
                            _logger.LogError(
                                "Prepared mesh {Status} for {Type} source " +
                                "0x{SourceId:X8}, runtime 0x{RuntimeId:X10}",
                                scan.Status,
                                req.Asset.Type,
                                req.Asset.SourceFileId,
                                req.Asset.RuntimeObjectId);
                        }
                    }
                    if (token.IsCancellationRequested)
                    {
                        wrapUpCanceled = true;
                    }
                    else if (blob is not null)
                    {
                        _cpuTriMeshStash.Store(blob);
                    }
                    if (!wrapUpCanceled && blob is not null && _ownership.IsPossessed(ident))

                        _linedTriMeshBlob.TryJuncture(blob);
                    if (!wrapUpCanceled)
                        wrapUpOutcome = blob;
                }
            }
            catch (OperationCanceledException exc)
            {
                wrapUpCanceled = true;
                wrapUpAbort = exc.CancellationToken;
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "Error preparing mesh data for 0x{Id:X8}", ident);
                wrapUpProblem = exc;
            }
            finally
            {
                lock (_queuedReqs)
                {
                    if (_engagedPrepByIdent.TryGetValue(ident, out PreparationAsk? engaged)
                        && ReferenceEquals(engaged, req))

                        _engagedPrepByIdent.Remove(ident);
                    if (wrapUpProblem is not null)
                        tcs.TrySetException(wrapUpProblem);
                    else if (wrapUpCanceled)
                        tcs.TrySetCanceled(wrapUpAbort.IsCancellationRequested
                            ? wrapUpAbort
                            : default);
                    else
                        tcs.TrySetResult(wrapUpOutcome);

                    if (_prepTasks.TryGetValue(ident, out Task<HarvestedMesh?>? latest)
                        && ReferenceEquals(latest, tcs.Task))

                        _prepTasks.TryRemove(ident, out _);
                    if (!wrapUpCanceled && (wrapUpProblem is not null || wrapUpOutcome is null))
                        _terminalPrepMisses.Add(ident);
                    else if (wrapUpOutcome is not null)
                        _terminalPrepMisses.Remove(ident);
                    if (!IsDisposed
                        && _queuedReqs.Count is not 0
                        && !_linedTriMeshBlob.IsAtHiWater
                        && !_arenaBackpressured)

                        _prepJobOnHand.Set();
                }
                req.TeardownAbort();
            }
        }
    }
}
