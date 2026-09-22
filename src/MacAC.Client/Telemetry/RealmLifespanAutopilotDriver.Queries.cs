using System.Text.Json;
using MacAC.Client.Graphics;
using MacAC.Client.Shell.Testing;

namespace MacAC.Client.Telemetry;

internal sealed partial class RealmLifespanAutopilotDriver
{
    public bool IsRealmReady => _fetchUnveil().IsReady;

    public bool IsRealmViewRectShown => _fetchUnveil().WorldViewportObserved;

    public bool IsScreenshotDone(string label) => _screenshots.IsComplete(label);

    public int GatewayMaterializationCount => _fetchGatewayMaterializationTally();

    public int RasterizeBundlePerformanceSpecimenTally =>
        _fetchRasterizeBundlePerformanceSpecimenTally();

    public bool RasterizeBundleFailedToCanon => _fetchRasterizeBundleFailedToCanon();

    public CanonWidgetAutopilotRenderPackStatus RasterizeBundleCondition =>
        _fetchRasterizeBundleCondition();

    public int FramebufferWidth => _fetchFramebufferDims().Width;

    public int FramebufferHeight => _fetchFramebufferDims().Height;

    public bool TryPickRasterizeBundle(string presetIdent, out string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetIdent);
        string normalized = presetIdent.ToLowerInvariant();
        if (normalized == "off")
            normalized = "retail";
        if (normalized is not ("retail" or "low" or "medium" or "high" or "auto"))
        {
            problem = $"unknown render-pack preset '{presetIdent}'";
            return false;
        }

        (bool succeeded, string pickProblem) = _pickRasterizeBundle(normalized);
        if (succeeded && normalized != "retail")
            _previousTurnedOnRasterizeBundlePreset = normalized;
        problem = pickProblem;
        return succeeded;
    }

    public bool TryDeactivateRasterizeBundle(out string problem)
    {
        if (_deactivateRasterizeBundle is not null)
        {
            (bool succeeded, string deactivateProblem) = _deactivateRasterizeBundle();
            problem = deactivateProblem;
            return succeeded;
        }
        var latest = RasterizeBundleCondition;
        if (latest.State == CanonWidgetAutopilotRenderPackPhase.Active
            && !string.Equals(latest.PackId, "retail", StringComparison.OrdinalIgnoreCase))

            _previousTurnedOnRasterizeBundlePreset = latest.PresetId;
        return TryPickRasterizeBundle("retail", out problem);
    }

    public bool TryReenableRasterizeBundle(out string problem)
    {
        if (_reenableRasterizeBundle is not null)
        {
            (bool succeeded, string reenableProblem) = _reenableRasterizeBundle();
            problem = reenableProblem;
            return succeeded;
        }
        if (string.IsNullOrWhiteSpace(_previousTurnedOnRasterizeBundlePreset))
        {
            problem = "render-pack re-enable requires a prior active enhanced selection";
            return false;
        }
        return TryPickRasterizeBundle(_previousTurnedOnRasterizeBundlePreset, out problem);
    }

    public bool TryRescaleFramebuffer(int width, int height, out string problem)
    {
        if (width < 320 || height < 240 || width > 8192 || height > 8192)
        {
            problem = "automation framebuffer size must be within 320x240 and 8192x8192";
            return false;
        }
        (bool succeeded, string rescaleProblem) = _rescaleFramebuffer(width, height);
        problem = rescaleProblem;
        return succeeded;
    }

    public bool TryRestartRasterizeBundlePerformance(out string problem)
    {
        if (RasterizeBundleFailedToCanon)
        {
            problem = string.Empty;
            return true;
        }
        (bool succeeded, string restartProblem) = _restartRasterizeBundlePerformance();
        problem = restartProblem;
        return succeeded;
    }

    public bool TryReqClientShut(out string problem)
    {
        if (_reqClientShut is null)
        {
            problem = "client-close automation is unavailable";
            return false;
        }

        try
        {
            _reqClientShut();
            problem = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            problem = $"client-close automation failed: {exception.Message}";
            return false;
        }
    }

    public bool TryReqCheckpoint(
        string label,
        out ICanonWidgetAutopilotCheckpoint? checkpoint,
        out string problem)
    {
        checkpoint = null;
        if (!AutopilotArtifactName.TryVet(label, out problem))
            return false;

        lock (_synchronize)
        {
            if (_destroyed)
            {
                problem = $"checkpoint '{label}' was rejected because world lifecycle automation is shutting down";
                return false;
            }

            var req = new RealmLifespanCheckpointAsk(
                _reqHolder,
                checked(++_series),
                label);
            _reqs.Enqueue(req);
            checkpoint = req;
            problem = string.Empty;
            return true;
        }
    }

    public bool TryReqScreenshot(string label, out string problem) =>
        _screenshots.TryReq(label, out problem);

    public void AbortCheckpoint(ICanonWidgetAutopilotCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint is not RealmLifespanCheckpointAsk req
            || !ReferenceEquals(req.Owner, _reqHolder))
        {
            throw new ArgumentException(
                "The checkpoint acknowledgement isn't owned by this automation runtime",
                nameof(checkpoint));
        }

        lock (_synchronize)
        {
            req.TrySetTerminal(
                CanonWidgetAutopilotCheckpointStatus.Cancelled,
                $"checkpoint '{req.Name}' was cancelled before render-frame capture");
        }
    }

    public void Process(RasterizeCycleFeed feed, RenderFrameVerdict verdict)
    {
        lock (_synchronize)
        {
            if (_destroyed)
                return;

            while (_reqs.Count is not 0)
            {
                var req = _reqs.Dequeue();
                if (req.Status != CanonWidgetAutopilotCheckpointStatus.Pending)
                    continue;
                EmitCheckpoint(req, verdict);
            }
        }
    }

    public void Dispose()
    {
        lock (_synchronize)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            while (_reqs.Count is not 0)
            {
                var req = _reqs.Dequeue();
                req.TrySetTerminal(
                    CanonWidgetAutopilotCheckpointStatus.Cancelled,
                    $"checkpoint '{req.Name}' was cancelled during world lifecycle automation shutdown");
            }
        }
    }

    public bool TryIsAutomationSignalPublished(
        string label,
        out bool published,
        out string problem)
    {
        published = false;
        if (!AutopilotArtifactName.TryVet(label, out problem))
            return false;

        published = File.Exists(Path.Combine(
            _artifactFolder,
            "signals",
            label + ".signal"));
        problem = string.Empty;
        return true;
    }

    private void EmitCheckpoint(
        RealmLifespanCheckpointAsk req,
        RenderFrameVerdict verdict)
    {
        try
        {
            Directory.CreateDirectory(_artifactFolder);
            RealmLifespanCheckpoint checkpoint = new RealmLifespanCheckpoint(
                Sequence: req.Sequence,
                Name: req.Name,
                TimestampUtc: DateTime.UtcNow,
                ProcessId: Environment.ProcessId,
                Reveal: _fetchUnveil(),
                EnvironmentOwnership: _fetchSurroundingsOwnership(),
                TransitOwnership: _fetchPassageOwnership(),
                Render: verdict,
                Resources: _grabAssetList(verdict));
            string json = JsonSerializer.Serialize(checkpoint, JsonKnobs);
            string timelineTrail = Path.Combine(
                _artifactFolder,
                "world-lifecycle.checkpoints.jsonl");
            File.WriteAllText(
                Path.Combine(
                    _artifactFolder,
                    $"checkpoint-{req.Name}.json"),
                json + Environment.NewLine);
            File.AppendAllText(timelineTrail, json + Environment.NewLine);
            req.TrySetTerminal(
                CanonWidgetAutopilotCheckpointStatus.Succeeded,
                problem: null);
            _trace(
                $"[world-gate] checkpoint name={req.Name} "
                + $"sequence={req.Sequence} path={timelineTrail}");
        }
        catch (Exception exception)
        {
            req.TrySetTerminal(
                CanonWidgetAutopilotCheckpointStatus.Failed,
                $"checkpoint '{req.Name}' sequence {req.Sequence} failed: "
                + exception.Message);
        }
    }
}
