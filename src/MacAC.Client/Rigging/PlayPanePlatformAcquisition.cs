namespace MacAC.Client.Rigging;

internal interface IPlayPanePlatformBulletin<in TGraphics, in TInput>
    where TGraphics : class
    where TInput : class
{
    void PublishGraphics(TGraphics visuals);
    void PublishInput(TInput feed);
}

internal sealed record PlayPanePlatformOutcome<TGraphics, TInput>(
    TGraphics Graphics,
    TInput Input)
    where TGraphics : class
    where TInput : class;

internal enum PlayPanePlatformAcquisitionPt
{
    GraphicsPublished,
    InputPublished,
}

internal static class PlayPanePlatformAcquisition
{
    public static PlayPanePlatformOutcome<TGraphics, TInput> Acquire<TGraphics, TInput>(
        Func<TGraphics> visualsMaker,
        Action<TGraphics> freeUnpublishedVisuals,
        Func<TInput> feedMaker,
        Action<TInput> freeUnpublishedFeed,
        IPlayPanePlatformBulletin<TGraphics, TInput> bulletin,
        Action<PlayPanePlatformAcquisitionPt>? flawInjection = null)
        where TGraphics : class
        where TInput : class
    {
        ArgumentNullException.ThrowIfNull(visualsMaker);
        ArgumentNullException.ThrowIfNull(freeUnpublishedVisuals);
        ArgumentNullException.ThrowIfNull(feedMaker);
        ArgumentNullException.ThrowIfNull(freeUnpublishedFeed);
        ArgumentNullException.ThrowIfNull(bulletin);

        AssemblyAcquisitionScope ambit = new AssemblyAcquisitionScope();
        try
        {
            var visualsTenancy = ambit.Acquire(
                "graphics API",
                visualsMaker,
                freeUnpublishedVisuals);
            TGraphics visuals = visualsTenancy.Publish(bulletin.PublishGraphics);
            flawInjection?.Invoke(PlayPanePlatformAcquisitionPt.GraphicsPublished);

            var feedTenancy = ambit.Acquire(
                "input context",
                feedMaker,
                freeUnpublishedFeed);
            TInput feed = feedTenancy.Publish(bulletin.PublishInput);
            flawInjection?.Invoke(PlayPanePlatformAcquisitionPt.InputPublished);

            ambit.Complete();
            return new PlayPanePlatformOutcome<TGraphics, TInput>(visuals, feed);
        }
        catch (Exception miss)
        {
            ambit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }
}
