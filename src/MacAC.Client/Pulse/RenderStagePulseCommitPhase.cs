using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Pulse;

// Closes the update boundary of the shadow engine, when the stage has one
internal sealed class RenderStagePulseCommitPhase(RenderStageShadeEngine? shade) : IPulseFrameCommitPhase
{
    public void Commit() => shade?.EmptyRefreshBoundary();
}
